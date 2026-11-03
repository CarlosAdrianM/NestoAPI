using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Infraestructure.Reposiciones;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#797 (Almacén, 03/11/26): las muestras (subgrupo MMP) sin código de barras se preparan, envían y reciben sin
    /// escanear (SinCodigoAdmitido), «Sin etiqueta» de una muestra no avisa a nadie y no salen en RepuestosSinCodigoBarras.
    /// </summary>
    [TestClass]
    public class MuestrasSinCodigoTests
    {
        // EAN-13 con el dígito de control bien (ValidadorGtin lo deja pasar)
        private const string GTIN_VALIDO = "4006381333931";

        [TestMethod]
        public void Admitido_SoloSinCodigoYMuestra_ConRellenoYMinusculas()
        {
            Assert.IsTrue(MuestrasSinCodigo.Admitido(true, "MMP"));
            Assert.IsTrue(MuestrasSinCodigo.Admitido(true, "mmp "), "Las columnas char van con relleno");
            Assert.IsFalse(MuestrasSinCodigo.Admitido(false, "MMP"), "Nunca con código");
            Assert.IsFalse(MuestrasSinCodigo.Admitido(true, "ACP"));
            Assert.IsFalse(MuestrasSinCodigo.Admitido(true, null));
        }

        // ---- Recorrido (picking y salida de reposición): CasadorEscaneos.OrdenarRecorrido ----

        [TestMethod]
        public void OrdenarRecorrido_MuestraSinCodigo_Admitido()
        {
            List<LineaPickingAlmacenDTO> lineas = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                new LineaPickingAlmacenDTO { Producto = "M1", SubGrupoProducto = "MMP", CodigoBarras = null, Cantidad = 1 },
                new LineaPickingAlmacenDTO { Producto = "M2", SubGrupoProducto = "MMP", CodigoBarras = GTIN_VALIDO, Cantidad = 1 },
                new LineaPickingAlmacenDTO { Producto = "M3", SubGrupoProducto = "MMP", CodigoBarras = "1", Cantidad = 1 },
                new LineaPickingAlmacenDTO { Producto = "P1", SubGrupoProducto = "ACP", CodigoBarras = null, Cantidad = 1 }
            });

            Assert.IsTrue(lineas.Single(l => l.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(lineas.Single(l => l.Producto == "M2").SinCodigoAdmitido, "Una muestra con código se escanea");
            Assert.IsTrue(lineas.Single(l => l.Producto == "M3").SinCodigoAdmitido, "Un código que no es GTIN cuenta como sin código (#674)");
            Assert.IsFalse(lineas.Single(l => l.Producto == "P1").SinCodigoAdmitido, "Lo que no es muestra sigue pidiendo etiqueta");
            Assert.IsTrue(lineas.Single(l => l.Producto == "P1").SinCodigo);
        }

        [TestMethod]
        public void RepartirLoResuelto_CopiaSinCodigoAdmitido()
        {
            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(new[]
            {
                new LineaPickingAlmacenDTO { Producto = "M1", SubGrupoProducto = "MMP", Cantidad = 2 }
            });

            List<LineaRecogidaDTO> recogida = CasadorEscaneos.RepartirLoResuelto(recorrido, new List<CasadorEscaneos.Cantidad>());

            Assert.IsTrue(recogida.Single().SinCodigoAdmitido);
        }

        [TestMethod]
        public void SqlRecorrido_TraeElSubgrupoYLaColumnaFija()
        {
            foreach (string sql in new[] { RepositorioPreparacionAlmacen.SQL_LINEAS_PICKING, RepositorioPreparacionAlmacen.SQL_LINEAS_REPOSICION_SALIDA })
            {
                StringAssert.Contains(sql, "AS SubGrupoProducto");
                StringAssert.Contains(sql, "CAST(0 AS bit) AS SinCodigoAdmitido");
            }
        }

        // ---- Packing ----

        [TestMethod]
        public void MontarPacking_MuestraSinCodigo_Admitido()
        {
            PackingAlmacenDTO packing = ServicioPreparacionAlmacen.MontarPacking("1", 99, new[]
            {
                Fila(1, "M1", "MMP", null),
                Fila(2, "M2", "MMP", GTIN_VALIDO),
                Fila(3, "P1", "ACP", null)
            });

            List<LineaPackingAlmacenDTO> lineas = packing.Entregas.Single().Pedidos.Single().Lineas;
            Assert.IsTrue(lineas.Single(l => l.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(lineas.Single(l => l.Producto == "M2").SinCodigoAdmitido);
            Assert.IsFalse(lineas.Single(l => l.Producto == "P1").SinCodigoAdmitido);
            StringAssert.Contains(RepositorioPreparacionAlmacen.SQL_LINEAS_PACKING, "RTRIM(p.SubGrupo) AS SubGrupo");
        }

        private static FilaPackingAlmacen Fila(int linea, string producto, string subGrupo, string codigo)
        {
            return new FilaPackingAlmacen
            {
                Pedido = 900, Cliente = "15191", Contacto = "0", LineaPedido = linea, Producto = producto, SubGrupo = subGrupo,
                CodigoBarras = codigo, Cantidad = 1
            };
        }

        // ---- Ubicar ----

        [TestMethod]
        public void MontarPendienteDeUbicar_MuestraSinCodigo_Admitido()
        {
            List<ProductoPendienteDeUbicarDTO> productos = ServicioUbicacionesAlmacen.MontarPendiente(new[]
            {
                new FilaPendienteDeUbicar { Producto = "M1", SubGrupo = "MMP", Cantidad = 1 },
                new FilaPendienteDeUbicar { Producto = "M2", SubGrupo = "MMP", CodigoBarras = GTIN_VALIDO, Cantidad = 1 },
                new FilaPendienteDeUbicar { Producto = "P1", SubGrupo = "ACP", Cantidad = 1 }
            }, new List<FilaUbicacionProducto>());

            Assert.IsTrue(productos.Single(p => p.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(productos.Single(p => p.Producto == "M2").SinCodigoAdmitido);
            Assert.IsFalse(productos.Single(p => p.Producto == "P1").SinCodigoAdmitido);
            StringAssert.Contains(RepositorioUbicacionesAlmacen.SQL_PENDIENTE_DE_UBICAR, "AS SubGrupo");
        }

        // ---- Recibir: reposición ----

        [TestMethod]
        public async Task RecibirReposicion_MuestraSinCodigo_Admitido()
        {
            var repositorio = A.Fake<IRepositorioRecepcionReposiciones>();
            A.CallTo(() => repositorio.LeerLineas("1", "REI", 81198)).Returns(new List<FilaReposicion>
            {
                new FilaReposicion { Producto = "M1", SubGrupo = "MMP", Cantidad = 2 },
                new FilaReposicion { Producto = "M2", SubGrupo = "MMP", CodigoBarras = GTIN_VALIDO, Cantidad = 1 },
                new FilaReposicion { Producto = "P1", SubGrupo = "ACP", Cantidad = 1 }
            });
            var servicio = new ServicioRecepcionReposiciones(repositorio);

            RecepcionReposicionDTO recepcion = await servicio.LeerRecepcion("1", "REI", 81198);

            Assert.IsTrue(recepcion.Lineas.Single(l => l.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(recepcion.Lineas.Single(l => l.Producto == "M2").SinCodigoAdmitido);
            Assert.IsFalse(recepcion.Lineas.Single(l => l.Producto == "P1").SinCodigoAdmitido);
            StringAssert.Contains(RepositorioRecepcionReposiciones.SQL_LINEAS, "AS SubGrupo");
        }

        [TestMethod]
        public async Task RecibirReposicion_LeerEsperado_PasaSinCodigoAdmitidoALaLinea()
        {
            var reposiciones = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => reposiciones.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 81198, Origen = "ALG", NombreOrigen = "Algete", Lineas = 2, Unidades = 2 }
            });
            A.CallTo(() => reposiciones.LeerRecepcion("1", "REI", 81198)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1",
                Almacen = "REI",
                Traspaso = 81198,
                Lineas = new List<LineaReposicionDTO>
                {
                    new LineaReposicionDTO { Producto = "M1", SinCodigo = true, SinCodigoAdmitido = true, Cantidad = 1 },
                    new LineaReposicionDTO { Producto = "P1", SinCodigo = true, Cantidad = 1 }
                }
            });
            var origen = new OrigenRecepcionReposiciones(reposiciones, A.Fake<IRepositorioCierreReposiciones>(), A.Fake<IAvisadorReposiciones>(),
                (e, u) => null, bultos: A.Fake<IRepositorioBultosReposicion>());

            RecepcionDTO recepcion = await origen.LeerEsperado("1", "REI", "81198");

            Assert.IsTrue(recepcion.Lineas.Single(l => l.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(recepcion.Lineas.Single(l => l.Producto == "P1").SinCodigoAdmitido);
        }

        // ---- Recibir: compras ----

        [TestMethod]
        public async Task RecibirCompra_MuestraSinCodigo_Admitido()
        {
            var repositorio = A.Fake<IRepositorioRecepcionCompras>();
            A.CallTo(() => repositorio.LeerLineasPendientesProveedor("1", "ALG", "65")).Returns(new List<FilaRecepcionCompra>
            {
                new FilaRecepcionCompra { Pedido = 100, LineaPedido = 1, Proveedor = "65", Producto = "M1", SubGrupo = "MMP ", Cantidad = 2 },
                new FilaRecepcionCompra { Pedido = 100, LineaPedido = 2, Proveedor = "65", Producto = "M2", SubGrupo = "MMP", CodigoBarras = GTIN_VALIDO, Cantidad = 1 },
                new FilaRecepcionCompra { Pedido = 100, LineaPedido = 3, Proveedor = "65", Producto = "P1", SubGrupo = "ACP", Cantidad = 1 }
            });
            var compras = new OrigenRecepcionCompras(repositorio, A.Fake<IAvisadorCompras>(), () => new DateTime(2026, 11, 3));

            RecepcionDTO recepcion = await compras.LeerEsperado("1", "ALG", "65");

            Assert.IsTrue(recepcion.Lineas.Single(l => l.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(recepcion.Lineas.Single(l => l.Producto == "M2").SinCodigoAdmitido);
            Assert.IsFalse(recepcion.Lineas.Single(l => l.Producto == "P1").SinCodigoAdmitido);
            StringAssert.Contains(RepositorioRecepcionCompras.SQL_LINEAS_PENDIENTES, "RTRIM(p.SubGrupo) AS SubGrupo");
        }

        // ---- Enviar reposición ----

        [TestMethod]
        public void EnviarReposicion_MuestraSinCodigo_Admitido()
        {
            var fecha = new DateTime(2026, 11, 3);
            ReposicionEnPreparacionDTO reposicion = ServicioPreparacionReposicion.Dto("1", "REI", "RepoReiAlg", new List<FilaReposicionEnPreparacion>
            {
                new FilaReposicionEnPreparacion { NumeroOrden = 1, Producto = "M1", SubGrupo = "MMP", Cantidad = 1, Almacen = "ALG", Fecha = fecha },
                new FilaReposicionEnPreparacion { NumeroOrden = 2, Producto = "M2", SubGrupo = "MMP", CodigoBarras = GTIN_VALIDO, Cantidad = 1, Almacen = "ALG", Fecha = fecha },
                new FilaReposicionEnPreparacion { NumeroOrden = 3, Producto = "P1", SubGrupo = "ACP", Cantidad = 1, Almacen = "ALG", Fecha = fecha }
            }, null);

            Assert.IsTrue(reposicion.Lineas.Single(l => l.Producto == "M1").SinCodigoAdmitido);
            Assert.IsFalse(reposicion.Lineas.Single(l => l.Producto == "M2").SinCodigoAdmitido);
            Assert.IsFalse(reposicion.Lineas.Single(l => l.Producto == "P1").SinCodigoAdmitido);
            StringAssert.Contains(RepositorioPreparacionReposicionSql.SQL_LINEAS_EN_PREPARACION, "RTRIM(pr.SubGrupo) AS SubGrupo");
        }

        // ---- Sin etiqueta ----

        [TestMethod]
        public async Task SinEtiqueta_MuestraSinCodigo_CuentaYNoAvisaANadie()
        {
            IServicioRecepcionReposiciones reposiciones = ReposicionConLinea(new LineaReposicionDTO
            {
                Producto = "M1", Descripcion = "MUESTRA CREMA", SinCodigo = true, SinCodigoAdmitido = true, Cantidad = 3
            });
            var avisador = A.Fake<IAvisadorSinEtiqueta>();
            var servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador);

            ResultadoSinEtiquetaRecepcionDTO resultado = await servicio.Apuntar("REPO", "1", "REI", "81198",
                new SinEtiquetaRecepcionDTO { Producto = "M1", Motivo = "NoLleva" }, @"NUEVAVISION\Reina");

            Assert.AreEqual("Apuntada 1 unidad sin etiqueta del M1: muestra sin código de barras: no se avisa a nadie.", resultado.Mensaje);
            Assert.AreEqual(0, resultado.Avisados.Count);
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task SinEtiqueta_ProductoSinCodigoQueNoEsMuestra_SigueAvisando()
        {
            IServicioRecepcionReposiciones reposiciones = ReposicionConLinea(new LineaReposicionDTO
            {
                Producto = "P1", SinCodigo = true, SinCodigoAdmitido = false, Cantidad = 3
            });
            var avisador = A.Fake<IAvisadorSinEtiqueta>();
            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._))
                .Returns(new List<string> { "Algete", "Compras" });
            var servicio = new ServicioSinEtiquetaRecepcion(reposiciones, avisador);

            ResultadoSinEtiquetaRecepcionDTO resultado = await servicio.Apuntar("REPO", "1", "REI", "81198",
                new SinEtiquetaRecepcionDTO { Producto = "P1", Motivo = "NoLleva" }, @"NUEVAVISION\Reina");

            A.CallTo(() => avisador.Avisar(A<string>._, A<string>._, A<string>._, A<string>._, A<string>._, A<IEnumerable<string>>._)).MustHaveHappenedOnceExactly();
            CollectionAssert.AreEqual(new[] { "Algete", "Compras" }, resultado.Avisados);
        }

        private static IServicioRecepcionReposiciones ReposicionConLinea(LineaReposicionDTO linea)
        {
            var reposiciones = A.Fake<IServicioRecepcionReposiciones>();
            A.CallTo(() => reposiciones.LeerRecepcion("1", "REI", 81198)).Returns(new RecepcionReposicionDTO
            {
                Empresa = "1", Almacen = "REI", Traspaso = 81198, Lineas = new List<LineaReposicionDTO> { linea }
            });
            A.CallTo(() => reposiciones.LeerPendientes("1", "REI")).Returns(new List<ReposicionPendienteDTO>
            {
                new ReposicionPendienteDTO { Traspaso = 81198, Origen = "ALG", NombreOrigen = "Algete" }
            });
            return reposiciones;
        }

        // ---- RepuestosSinCodigoBarras ----

        [TestMethod]
        public void RepuestosSinCodigoBarras_SinMuestras()
        {
            const string FILTRO = "ISNULL(RTRIM(p.SubGrupo), '') <> 'MMP'";
            StringAssert.Contains(RepositorioRepuestosSinCodigoSql.SQL_REPUESTOS, FILTRO);

            string raiz = System.IO.Path.GetDirectoryName(typeof(MuestrasSinCodigoTests).Assembly.Location);
            while (raiz != null && !System.IO.Directory.Exists(System.IO.Path.Combine(raiz, "Scripts")))
            {
                raiz = System.IO.Path.GetDirectoryName(raiz);
            }
            Assert.IsNotNull(raiz, "No se encuentra la carpeta Scripts del repositorio");
            string script = System.IO.File.ReadAllText(System.IO.Path.Combine(raiz, "Scripts", "Issue762_ProductosRepuestosSinCodigo.sql"));
            StringAssert.Contains(script, FILTRO);
        }
    }
}
