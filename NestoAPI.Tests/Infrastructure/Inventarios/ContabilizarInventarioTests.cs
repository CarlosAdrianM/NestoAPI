using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Inventarios;
using NestoAPI.Models.Inventarios;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Inventarios
{
    /// <summary>
    /// NestoAPI#790 (épica #784): las comprobaciones de antes de contabilizar el inventario (las consultas de Carlos), contabilizar
    /// usuario a usuario con prdContabilizarInventario y las diferencias valoradas.
    /// </summary>
    [TestClass]
    public class ComprobadorInventarioTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 21);

        private static FilaInventarioPendiente Fila(Action<FilaInventarioPendiente> cambiar = null, int orden = 1)
        {
            var fila = new FilaInventarioPendiente
            {
                NumeroOrden = orden,
                Producto = "17404",
                Descripcion = "PRODUCTO",
                StockCalculado = 3,
                StockReal = 3,
                Pasillo = "A01",
                Fila = "001",
                Columna = "001",
                Usuario = "NUEVAVISION\\Andre",
                Fecha = DIA.AddHours(10),
                Grupo = "COS",
                Ubicar = true,
                EstadoProducto = 0,
                PrecioMedio = 2.5M
            };
            cambiar?.Invoke(fila);
            return fila;
        }

        private static UbicacionAlmacenInventario Ubicacion(string pasillo = "A01", string fila = "001", string columna = "001", int cantidad = 3,
            string producto = "17404") =>
            new UbicacionAlmacenInventario { Pasillo = pasillo, Fila = fila, Columna = columna, Cantidad = cantidad, Producto = producto };

        private static ComprobacionesInventarioDTO Comprobar(bool controlUbicaciones, params FilaInventarioPendiente[] filas) =>
            ComprobadorInventario.Comprobar("ALG", DIA, controlUbicaciones, filas, new[] { Ubicacion() });

        private static ComprobacionesInventarioDTO Comprobar(bool controlUbicaciones, IEnumerable<UbicacionAlmacenInventario> ubicaciones,
            params FilaInventarioPendiente[] filas) =>
            ComprobadorInventario.Comprobar("ALG", DIA, controlUbicaciones, filas, ubicaciones);

        private static bool Tiene(List<ComprobacionInventarioDTO> lista, string codigo) => lista.Any(c => c.Codigo == codigo);

        [TestMethod]
        public void UnaFilaCorrecta_NoTieneNiBloqueantesNiAvisos()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila());

            Assert.AreEqual(0, r.Bloqueantes.Count, string.Join(", ", r.Bloqueantes.Select(b => b.Codigo)));
            Assert.AreEqual(0, r.Avisos.Count, string.Join(", ", r.Avisos.Select(b => b.Codigo)));
            Assert.AreEqual("ALG", r.Almacen);
            Assert.AreEqual(DIA, r.Fecha);
        }

        [TestMethod]
        public void OtraFecha_UnaPendienteDeOtroDia_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(), Fila(f => f.Fecha = DIA.AddDays(-30), orden: 2));

            ComprobacionInventarioDTO otra = r.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.OTRA_FECHA);
            Assert.AreEqual(2, otra.Filas.Single().NºOrden);
        }

        [TestMethod]
        public void OtraFecha_ElMismoDiaAOtraHora_NoBloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => f.Fecha = DIA.AddHours(23).AddMinutes(59)));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.OTRA_FECHA));
        }

        [TestMethod]
        public void CantidadDisparatada_MasDe9999_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.StockReal = 10000; f.StockCalculado = 2; }));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.CANTIDAD_DISPARATADA));
        }

        [TestMethod]
        public void CantidadDisparatada_9999_NoBloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.StockReal = 9999; f.StockCalculado = 2; }));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.CANTIDAD_DISPARATADA));
        }

        [TestMethod]
        public void CantidadDisparatada_LaReferenciaDeUnProductoLeidaComoCantidad_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.StockReal = 40945; f.StockRealEsReferencia = true; }));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.CANTIDAD_DISPARATADA));
        }

        [TestMethod]
        public void CantidadDisparatada_SiCoincideConElCalculado_NoBloquea()
        {
            // En ALG hay un producto con 17.686 unidades de verdad: contarlas bien no es un error de lectura
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.StockReal = 17686; f.StockCalculado = 17686; }));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.CANTIDAD_DISPARATADA));
        }

        [TestMethod]
        public void Ficticio_ConStockReal_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.Ficticio = true; f.StockReal = 1; }));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.FICTICIO));
        }

        [TestMethod]
        public void Ficticio_ConStockRealCero_NoBloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.Ficticio = true; f.StockReal = 0; }));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.FICTICIO));
        }

        [TestMethod]
        public void UbicacionMalFormada_PasilloDeDosCaracteres_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(f => f.Pasillo = "A1"));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_MAL_FORMADA));
            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_INEXISTENTE), "Una mal escrita no se cuenta dos veces");
        }

        [TestMethod]
        public void UbicacionMalFormada_PasilloConFilaNula_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(f => f.Fila = null));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_MAL_FORMADA));
        }

        [TestMethod]
        public void UbicacionMalFormada_TresCaracteres_NoBloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila());

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_MAL_FORMADA));
        }

        [TestMethod]
        public void SinUbicacion_ProductoQueSeUbica_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(f => { f.Pasillo = null; f.Fila = null; f.Columna = null; }));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.SIN_UBICACION));
            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_MAL_FORMADA));
        }

        [TestMethod]
        public void SinUbicacion_ProductoQueNoSeUbica_NoBloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(f => { f.Pasillo = null; f.Fila = null; f.Columna = null; f.Ubicar = false; }));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.SIN_UBICACION));
        }

        [TestMethod]
        public void UbicacionInexistente_NoEstaEnUbicaciones_Bloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(f => f.Pasillo = "Z99"));

            Assert.IsTrue(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_INEXISTENTE));
        }

        [TestMethod]
        public void UbicacionInexistente_ExisteConOtroProducto_NoBloquea()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, new[] { Ubicacion(producto: "99999") }, Fila());

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.UBICACION_INEXISTENTE));
        }

        [TestMethod]
        public void SinControlDeUbicaciones_NoEvaluaNingunaDeUbicaciones()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, new[] { Ubicacion("B02") },
                Fila(f => f.Pasillo = "A1"),
                Fila(f => { f.Pasillo = null; f.Fila = null; f.Columna = null; }, 2),
                Fila(f => f.Pasillo = "Z99", 3),
                Fila(f => f.Grupo = "PEL", 4));

            Assert.AreEqual(0, r.Bloqueantes.Count, string.Join(", ", r.Bloqueantes.Select(b => b.Codigo)));
            Assert.AreEqual(0, r.Avisos.Count, string.Join(", ", r.Avisos.Select(b => b.Codigo)));
            Assert.IsFalse(r.ControlUbicaciones);
        }

        [TestMethod]
        public void AnuladoConStock_Avisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.EstadoProducto = -1; f.StockReal = 2; }));

            Assert.IsTrue(Tiene(r.Avisos, CodigosComprobacionInventario.ANULADO_CON_STOCK));
            Assert.AreEqual(0, r.Bloqueantes.Count);
        }

        [TestMethod]
        public void AnuladoSinStock_NoAvisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => { f.EstadoProducto = -1; f.StockReal = 0; }));

            Assert.IsFalse(Tiene(r.Avisos, CodigosComprobacionInventario.ANULADO_CON_STOCK));
        }

        [TestMethod]
        public void SinPrecioMedio_NuloOCeroConStock_Avisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => f.PrecioMedio = null), Fila(f => f.PrecioMedio = 0, 2));

            Assert.AreEqual(2, r.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.SIN_PRECIO_MEDIO).Filas.Count);
        }

        [TestMethod]
        public void SinPrecioMedio_ConPrecioOSinStock_NoAvisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(), Fila(f => { f.PrecioMedio = null; f.StockReal = 0; }, 2));

            Assert.IsFalse(Tiene(r.Avisos, CodigosComprobacionInventario.SIN_PRECIO_MEDIO));
        }

        [TestMethod]
        public void CalculadoNegativo_Avisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => f.StockCalculado = -2));

            Assert.IsTrue(Tiene(r.Avisos, CodigosComprobacionInventario.CALCULADO_NEGATIVO));
        }

        [TestMethod]
        public void CalculadoCero_NoAvisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(f => f.StockCalculado = 0));

            Assert.IsFalse(Tiene(r.Avisos, CodigosComprobacionInventario.CALCULADO_NEGATIVO));
        }

        [TestMethod]
        public void UbicacionSinRellenar_UbicacionConStockSinFila_Avisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, new[] { Ubicacion(), Ubicacion("B02", cantidad: 7, producto: "40945") }, Fila());

            FilaComprobacionInventarioDTO fila = r.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.UBICACION_SIN_RELLENAR).Filas.Single();
            Assert.AreEqual("B02", fila.Pasillo);
            Assert.AreEqual("40945", fila.Producto);
            Assert.AreEqual(7, fila.StockCalculado);
            Assert.IsNull(fila.NºOrden);
        }

        [TestMethod]
        public void UbicacionSinRellenar_InventariadaOSinStock_NoAvisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, new[] { Ubicacion(producto: "OTRO"), Ubicacion("B02", cantidad: 0) }, Fila());

            Assert.IsFalse(Tiene(r.Avisos, CodigosComprobacionInventario.UBICACION_SIN_RELLENAR));
        }

        [TestMethod]
        public void GruposMezclados_MismaUbicacionConGruposDistintos_Avisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(), Fila(f => { f.Grupo = "PEL"; f.Producto = "40945"; }, 2));

            Assert.AreEqual(2, r.Avisos.Single(a => a.Codigo == CodigosComprobacionInventario.GRUPOS_MEZCLADOS).Filas.Count);
        }

        [TestMethod]
        public void GruposMezclados_MismoGrupo_NoAvisa()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(), Fila(f => f.Producto = "40945", 2));

            Assert.IsFalse(Tiene(r.Avisos, CodigosComprobacionInventario.GRUPOS_MEZCLADOS));
        }

        // ---------- NestoAPI#784 (09/11/26): dos filas del mismo producto y el «contado 0» de un evolutivo abierto sin contar ----------

        private static FilaInventarioPendiente Tienda(string producto, int orden, int calculado, int real) =>
            Fila(f => { f.Producto = producto; f.Pasillo = null; f.Fila = null; f.Columna = null; f.StockCalculado = calculado; f.StockReal = real; }, orden);

        [TestMethod]
        public void FilasDuplicadas_Tienda_MismoProductoDosFilasEseDia_Bloquea_784()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Tienda("42341", 948780, 0, 2), Tienda("42341", 948790, 2, 0), Tienda("17404", 948791, 3, 3));

            ComprobacionInventarioDTO duplicadas = r.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.FILAS_DUPLICADAS);
            CollectionAssert.AreEqual(new int?[] { 948780, 948790 }, duplicadas.Filas.Select(f => f.NºOrden).ToArray());
            Assert.IsFalse(duplicadas.SeConfirma);
        }

        [TestMethod]
        public void FilasDuplicadas_Algete_MismaUbicacion_Bloquea_784()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(), Fila(orden: 2));

            Assert.AreEqual(2, r.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.FILAS_DUPLICADAS).Filas.Count);
        }

        [TestMethod]
        public void FilasDuplicadas_Algete_MismoProductoEnOtraUbicacion_NoBloquea_784()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, new[] { Ubicacion(), Ubicacion(columna: "002") },
                Fila(), Fila(f => f.Columna = "002", 2));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.FILAS_DUPLICADAS));
        }

        [TestMethod]
        public void FilasDuplicadas_UnaDeOtroDia_NoEsDuplicada_784()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Fila(), Fila(f => f.Fecha = DIA.AddDays(-1), 2));

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.FILAS_DUPLICADAS));
        }

        [TestMethod]
        public void ContadoCeroConStock_Tienda_Avisa_784()
        {
            ComprobacionesInventarioDTO r = Comprobar(false, Tienda("17404", 1, 2, 0), Tienda("29608", 2, 0, 0), Tienda("29613", 3, -1, 0));

            ComprobacionInventarioDTO aviso = r.Avisos.Single(b => b.Codigo == CodigosComprobacionInventario.CONTADO_CERO_CON_STOCK);
            Assert.AreEqual("17404", aviso.Filas.Single().Producto);
            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.CONTADO_CERO_CON_STOCK));
        }

        [TestMethod]
        public void ContadoCeroConStock_Algete_NoAvisa_784()
        {
            ComprobacionesInventarioDTO r = Comprobar(true, Fila(f => { f.StockCalculado = 2; f.StockReal = 0; }));

            Assert.IsFalse(Tiene(r.Avisos, CodigosComprobacionInventario.CONTADO_CERO_CON_STOCK));
        }

        [TestMethod]
        public void RecontadaSinContar_Tienda_ProductoYaContabilizadoEseDiaConContadoCero_BloqueaYSeConfirma_784()
        {
            var contabilizadas = new[] { new FilaInventarioContabilizada { NumeroOrden = 948780, Producto = "42341", StockReal = 2, Traspaso = 81350, Fecha = DIA } };

            ComprobacionesInventarioDTO r = ComprobadorInventario.Comprobar("ALC", DIA, false,
                new[] { Tienda("42341", 948790, 2, 0), Tienda("17404", 948791, 2, 0) }, null, contabilizadas);

            ComprobacionInventarioDTO recontada = r.Bloqueantes.Single(b => b.Codigo == CodigosComprobacionInventario.RECONTADA_SIN_CONTAR);
            Assert.AreEqual(948790, recontada.Filas.Single().NºOrden);
            Assert.IsTrue(recontada.SeConfirma);
        }

        [TestMethod]
        public void RecontadaSinContar_SiSeHaContadoAlgo_NoBloquea_784()
        {
            var contabilizadas = new[] { new FilaInventarioContabilizada { NumeroOrden = 948780, Producto = "42341", StockReal = 2, Traspaso = 81350, Fecha = DIA } };

            ComprobacionesInventarioDTO r = ComprobadorInventario.Comprobar("ALC", DIA, false,
                new[] { Tienda("42341", 948790, 2, 1) }, null, contabilizadas);

            Assert.IsFalse(Tiene(r.Bloqueantes, CodigosComprobacionInventario.RECONTADA_SIN_CONTAR));
        }

        [TestMethod]
        public void Recuento_FilasDelDiaPorUsuario()
        {
            ComprobacionesInventarioDTO r = Comprobar(false,
                Fila(), Fila(orden: 2), Fila(f => f.Usuario = "NUEVAVISION\\Pedro", 3), Fila(f => f.Fecha = DIA.AddDays(-1), 4));

            Assert.AreEqual(2, r.Recuento.Count);
            Assert.AreEqual(2, r.Recuento.Single(u => u.Usuario == "NUEVAVISION\\Andre").Filas);
            Assert.AreEqual(1, r.Recuento.Single(u => u.Usuario == "NUEVAVISION\\Pedro").Filas);
        }
    }

    [TestClass]
    public class ServicioContabilizarInventarioTests
    {
        private static readonly DateTime DIA = new DateTime(2026, 11, 21);
        private IRepositorioContabilizarInventario repositorio;
        private ServicioContabilizarInventario servicio;

        private static FilaInventarioPendiente Fila(string usuario, int orden, Action<FilaInventarioPendiente> cambiar = null)
        {
            var fila = new FilaInventarioPendiente
            {
                NumeroOrden = orden, Producto = "17404", StockCalculado = 1, StockReal = 2, Usuario = usuario, Fecha = DIA,
                Grupo = "COS", PrecioMedio = 1
            };
            cambiar?.Invoke(fila);
            return fila;
        }

        [TestInitialize]
        public void Inicializar()
        {
            repositorio = A.Fake<IRepositorioContabilizarInventario>();
            A.CallTo(() => repositorio.LeerControlUbicaciones("1", "REI")).Returns(Task.FromResult<bool?>(false));
            A.CallTo(() => repositorio.Contabilizar(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(1));
            servicio = new ServicioContabilizarInventario(repositorio);
        }

        private void Pendientes(params FilaInventarioPendiente[] filas) =>
            A.CallTo(() => repositorio.LeerPendientes("1", "REI")).Returns(Task.FromResult(filas.ToList()));

        [TestMethod]
        public async Task Comprobar_AlmacenQueNoExiste_DevuelveNull()
        {
            A.CallTo(() => repositorio.LeerControlUbicaciones("1", "XXX")).Returns(Task.FromResult<bool?>(null));

            Assert.IsNull(await servicio.Comprobar("1", "XXX", DIA));
        }

        [TestMethod]
        public async Task Comprobar_SinControlDeUbicaciones_NoLeeLasUbicaciones()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 1));

            _ = await servicio.Comprobar("1", "REI", DIA);

            A.CallTo(() => repositorio.LeerUbicaciones(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_ConControlDeUbicaciones_LeeLasDelAlmacen()
        {
            A.CallTo(() => repositorio.LeerControlUbicaciones("1", "ALG")).Returns(Task.FromResult<bool?>(true));
            A.CallTo(() => repositorio.LeerPendientes("1", "ALG")).Returns(Task.FromResult(new List<FilaInventarioPendiente>()));

            ComprobacionesInventarioDTO r = await servicio.Comprobar("1", "ALG", DIA);

            A.CallTo(() => repositorio.LeerUbicaciones("ALG")).MustHaveHappenedOnceExactly();
            Assert.IsTrue(r.ControlUbicaciones);
        }

        [TestMethod]
        public async Task Contabilizar_ConBloqueantes_NoLlamaAlProcedimiento()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 1), Fila("NUEVAVISION\\Paloma", 2, f => f.Fecha = DIA.AddDays(-3)));

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA);

            Assert.IsNotNull(r.Bloqueado);
            Assert.IsNull(r.Contabilizacion);
            Assert.IsTrue(r.Bloqueado.Bloqueantes.Any(b => b.Codigo == CodigosComprobacionInventario.OTRA_FECHA));
            A.CallTo(() => repositorio.Contabilizar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizar_SinBloqueantes_UnaVezPorUsuarioConSuAlmacen()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 1), Fila("NUEVAVISION\\Paloma", 2, f => f.Producto = "29608"), Fila("NUEVAVISION\\Laura", 3, f => f.Producto = "29613"));
            A.CallTo(() => repositorio.LeerTraspaso("1", "REI", "NUEVAVISION\\Paloma", DIA)).Returns(Task.FromResult<int?>(80001));
            A.CallTo(() => repositorio.LeerTraspaso("1", "REI", "NUEVAVISION\\Laura", DIA)).Returns(Task.FromResult<int?>(80002));

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA);

            A.CallTo(() => repositorio.Contabilizar("1", "NUEVAVISION\\Paloma", "REI")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Contabilizar("1", "NUEVAVISION\\Laura", "REI")).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.Contabilizar(A<string>._, A<string>._, A<string>._)).MustHaveHappenedTwiceExactly();
            Assert.IsNull(r.Bloqueado);
            Assert.AreEqual(0, r.Contabilizacion.Errores.Count);
            Assert.AreEqual(80001, r.Contabilizacion.Contabilizados.Single(c => c.Usuario == "NUEVAVISION\\Paloma").Traspaso);
            Assert.AreEqual(2, r.Contabilizacion.Contabilizados.Single(c => c.Usuario == "NUEVAVISION\\Paloma").Filas);
            Assert.AreEqual(80002, r.Contabilizacion.Contabilizados.Single(c => c.Usuario == "NUEVAVISION\\Laura").Traspaso);
            Assert.AreEqual("REI", r.Contabilizacion.Almacen);
        }

        private void ContabilizadaHoy42341() =>
            A.CallTo(() => repositorio.LeerContabilizadas("1", "REI", DIA)).Returns(Task.FromResult(new List<FilaInventarioContabilizada>
            {
                new FilaInventarioContabilizada { NumeroOrden = 948780, Producto = "42341", StockReal = 2, Traspaso = 81350, Fecha = DIA }
            }));

        [TestMethod]
        public async Task Contabilizar_RecontadaSinContar_SinConfirmar_NoLlamaAlProcedimiento_784()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 948790, f => { f.Producto = "42341"; f.StockCalculado = 2; f.StockReal = 0; }));
            ContabilizadaHoy42341();

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA, false);

            Assert.IsTrue(r.Bloqueado.Bloqueantes.Any(b => b.Codigo == CodigosComprobacionInventario.RECONTADA_SIN_CONTAR));
            A.CallTo(() => repositorio.Contabilizar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizar_RecontadaSinContar_Confirmando_Contabiliza_784()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 948790, f => { f.Producto = "42341"; f.StockCalculado = 2; f.StockReal = 0; }));
            ContabilizadaHoy42341();

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA, true);

            Assert.IsNull(r.Bloqueado);
            A.CallTo(() => repositorio.Contabilizar("1", "NUEVAVISION\\Paloma", "REI")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Contabilizar_FilasDuplicadas_NiConfirmando_784()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 1), Fila("NUEVAVISION\\Paloma", 2));

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA, true);

            Assert.IsTrue(r.Bloqueado.Bloqueantes.Any(b => b.Codigo == CodigosComprobacionInventario.FILAS_DUPLICADAS));
            A.CallTo(() => repositorio.Contabilizar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Comprobar_ConControlDeUbicaciones_NoLeeLasContabilizadas_784()
        {
            A.CallTo(() => repositorio.LeerControlUbicaciones("1", "ALG")).Returns(Task.FromResult<bool?>(true));

            _ = await servicio.Comprobar("1", "ALG", DIA);

            A.CallTo(() => repositorio.LeerContabilizadas(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizar_ConSoloAvisos_Contabiliza()
        {
            Pendientes(Fila("NUEVAVISION\\Paloma", 1, f => f.PrecioMedio = null));

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA);

            Assert.IsNull(r.Bloqueado);
            A.CallTo(() => repositorio.Contabilizar("1", "NUEVAVISION\\Paloma", "REI")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Contabilizar_UnUsuarioQueFalla_NoParaALosDemas()
        {
            Pendientes(Fila("NUEVAVISION\\Laura", 1), Fila("NUEVAVISION\\Paloma", 2, f => f.Producto = "29608"));
            A.CallTo(() => repositorio.Contabilizar("1", "NUEVAVISION\\Laura", "REI"))
                .Throws(new InvalidOperationException("El usuario NUEVAVISION\\Laura tiene 3 filas pendientes en ALC: contabilice cada almacén por separado"));
            A.CallTo(() => repositorio.LeerTraspaso("1", "REI", "NUEVAVISION\\Paloma", DIA)).Returns(Task.FromResult<int?>(80003));

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA);

            ErrorContabilizarInventarioDTO error = r.Contabilizacion.Errores.Single();
            Assert.AreEqual("NUEVAVISION\\Laura", error.Usuario);
            StringAssert.Contains(error.Mensaje, "contabilice cada almacén por separado");
            Assert.AreEqual(80003, r.Contabilizacion.Contabilizados.Single().Traspaso);
            A.CallTo(() => repositorio.LeerTraspaso("1", "REI", "NUEVAVISION\\Laura", A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Contabilizar_ElProcedimientoDevuelveMenosUno_EsUnError()
        {
            Pendientes(Fila("NUEVAVISION\\Laura", 1));
            A.CallTo(() => repositorio.Contabilizar("1", "NUEVAVISION\\Laura", "REI")).Returns(Task.FromResult(-1));

            ResultadoContabilizarInventario r = await servicio.Contabilizar("1", "REI", DIA);

            Assert.AreEqual(0, r.Contabilizacion.Contabilizados.Count);
            Assert.AreEqual("NUEVAVISION\\Laura", r.Contabilizacion.Errores.Single().Usuario);
        }

        [TestMethod]
        public async Task Diferencias_ConTraspasos_NoFiltraPorFecha()
        {
            A.CallTo(() => repositorio.LeerDiferencias(A<string>._, A<string>._, A<DateTime?>._, A<IList<int>>._))
                .Returns(Task.FromResult(new List<LineaInventarioConDiferencia>()));

            _ = await servicio.Diferencias("1", "REI", DIA, new List<int> { 80001, 80002 }, PermisosInventario.VALORACION_PVP);

            A.CallTo(() => repositorio.LeerDiferencias("1", "REI", null, A<IList<int>>.That.Matches(t => t.Count == 2))).MustHaveHappenedOnceExactly();
        }

        private static List<LineaInventarioConDiferencia> Lineas() => new List<LineaInventarioConDiferencia>
        {
            // sobra 2 de COS a PVP 10 / coste 4
            new LineaInventarioConDiferencia { Producto = "A", Grupo = "COS", Familia = "Eva", StockCalculado = 1, StockReal = 3, PVP = 10M, Coste = 4M },
            // faltan 3 de COS a PVP 5 / coste 2.5
            new LineaInventarioConDiferencia { Producto = "B", Grupo = "COS", Familia = "Lisap", StockCalculado = 5, StockReal = 2, PVP = 5M, Coste = 2.5M },
            // falta 1 de PEL a PVP 20 / coste 8.333
            new LineaInventarioConDiferencia { Producto = "C", Grupo = "PEL", Familia = "Lisap", StockCalculado = 1, StockReal = 0, PVP = 20M, Coste = 8.333M }
        };

        [TestMethod]
        public void CalcularDiferencias_APvp_ValoraConElPvp()
        {
            DiferenciasInventarioDTO d = ServicioContabilizarInventario.CalcularDiferencias("REI", DIA, new List<int>(), true, Lineas());

            Assert.AreEqual("PVP", d.Valoracion);
            Assert.AreEqual(10M, d.Lineas.Single(l => l.Producto == "A").ValorUnitario);
            Assert.AreEqual(20M, d.Lineas.Single(l => l.Producto == "A").ValorDiferencia);
            Assert.AreEqual(3, d.Totales.Filas);
            Assert.AreEqual(-2, d.Totales.Unidades);
            Assert.AreEqual(20M - 15M - 20M, d.Totales.Valor);
            Assert.AreEqual(-1, d.PorGrupo.Single(g => g.Grupo == "COS").Unidades);
            Assert.AreEqual(5M, d.PorGrupo.Single(g => g.Grupo == "COS").Valor);
            Assert.AreEqual(-20M, d.PorGrupo.Single(g => g.Grupo == "PEL").Valor);
            Assert.AreEqual(-35M, d.PorFamilia.Single(f => f.Familia == "Lisap").Valor);
        }

        [TestMethod]
        public void CalcularDiferencias_ACoste_ValoraConElPrecioMedioYRedondea()
        {
            DiferenciasInventarioDTO d = ServicioContabilizarInventario.CalcularDiferencias("ALG", DIA, new List<int>(), false, Lineas());

            Assert.AreEqual("Coste", d.Valoracion);
            Assert.AreEqual(-7.5M, d.Lineas.Single(l => l.Producto == "B").ValorDiferencia);
            Assert.AreEqual(-8.33M, d.Lineas.Single(l => l.Producto == "C").ValorDiferencia);
            Assert.AreEqual(8M - 7.5M - 8.33M, d.Totales.Valor);
            Assert.AreEqual("C", d.Lineas.First().Producto, "Ordenadas por valor de la diferencia (en absoluto) de mayor a menor");
        }
    }
}
