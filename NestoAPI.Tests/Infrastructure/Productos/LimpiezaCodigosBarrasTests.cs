using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Productos
{
    /// <summary>NestoAPI#776: dejar en blanco los códigos de barras que no son válidos.</summary>
    [TestClass]
    public class LimpiezaCodigosBarrasTests
    {
        private const string EAN13_BUENO = "8437020512176";
        private const string EAN13_CONTROL_MAL = "8437020512177";

        /// <summary>Repositorio en memoria: Limpiar quita de verdad lo limpiado y guarda la auditoría que se grabaría.</summary>
        private class RepositorioFalso : IRepositorioLimpiezaCodigosBarras
        {
            public List<FichaCodigoBarrasLimpieza> Fichas { get; } = new List<FichaCodigoBarrasLimpieza>();
            public List<FilaCodigoBarrasLimpieza> Filas { get; } = new List<FilaCodigoBarrasLimpieza>();
            public List<Modificacion> Modificaciones { get; } = new List<Modificacion>();
            public int Llamadas { get; private set; }

            public List<FichaCodigoBarrasLimpieza> LeerFichasConCodigo() => Fichas.Where(f => !string.IsNullOrWhiteSpace(f.CodBarras)).ToList();

            public List<FilaCodigoBarrasLimpieza> LeerFilasTabla() => Filas.ToList();

            public int Limpiar(IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO> lote, string usuario)
            {
                Llamadas++;
                int limpiados = 0;
                foreach (CandidatoLimpiezaCodigoBarrasDTO c in lote)
                {
                    if (c.Donde == InformeCodigosBarras.DONDE_FICHA)
                    {
                        FichaCodigoBarrasLimpieza ficha = Fichas.SingleOrDefault(f => f.Empresa == c.Empresa && f.Producto == c.Producto && f.CodBarras?.Trim() == c.Codigo);
                        if (ficha == null)
                        {
                            continue;
                        }
                        ficha.CodBarras = null;
                    }
                    else if (Filas.RemoveAll(f => f.Id == c.Id && f.Codigo?.Trim() == c.Codigo) == 0)
                    {
                        continue;
                    }
                    Modificaciones.Add(ServicioLimpiezaCodigosBarras.Auditoria(c, usuario));
                    limpiados++;
                }
                return limpiados;
            }
        }

        private static FichaCodigoBarrasLimpieza Ficha(string producto, string codigo, short estado = 0, string empresa = "1")
            => new FichaCodigoBarrasLimpieza { Empresa = empresa, Producto = producto, Nombre = "PRODUCTO " + producto, Estado = estado, CodBarras = codigo };

        private static FilaCodigoBarrasLimpieza Fila(int id, string producto, string codigo, short? estado = 0, string empresa = "1")
            => new FilaCodigoBarrasLimpieza { Id = id, Empresa = empresa, Producto = producto, Codigo = codigo, Nombre = "PRODUCTO " + producto, Estado = estado };

        private static RepositorioFalso ConDatos()
        {
            var repositorio = new RepositorioFalso();
            repositorio.Fichas.Add(Ficha("100", "1"));
            repositorio.Fichas.Add(Ficha("101", "0  "));
            repositorio.Fichas.Add(Ficha("102", "123"));
            repositorio.Fichas.Add(Ficha("103", EAN13_CONTROL_MAL, estado: -1));
            repositorio.Fichas.Add(Ficha("104", EAN13_BUENO));
            repositorio.Fichas.Add(Ficha("105", "   "));
            repositorio.Fichas.Add(Ficha("106", null));
            repositorio.Filas.Add(Fila(1, "100", "1"));             // la copia de la ficha que hizo el trigger
            repositorio.Filas.Add(Fila(2, "104", EAN13_BUENO));
            repositorio.Filas.Add(Fila(3, "104", "0"));
            repositorio.Filas.Add(Fila(4, "17404", CodigoBarrasInterno.DeReferencia("17404")));
            repositorio.Filas.Add(Fila(5, "999", "12", estado: null)); // el producto ya no está en Productos
            return repositorio;
        }

        [TestMethod]
        public void MotivoNoValido_DiceElPorQue_YNullSiVale()
        {
            Assert.IsNull(ValidadorGtin.MotivoNoValido(EAN13_BUENO));
            Assert.AreEqual(ValidadorGtin.MOTIVO_VACIO, ValidadorGtin.MotivoNoValido("  "));
            Assert.AreEqual("Tiene 1 cifra: un código de barras tiene 8, 12, 13 o 14", ValidadorGtin.MotivoNoValido("1"));
            Assert.AreEqual("Tiene 3 cifras: un código de barras tiene 8, 12, 13 o 14", ValidadorGtin.MotivoNoValido("123"));
            Assert.AreEqual(ValidadorGtin.MOTIVO_NO_SOLO_CIFRAS, ValidadorGtin.MotivoNoValido("1027VF"));
            Assert.AreEqual(ValidadorGtin.MOTIVO_TODO_CEROS, ValidadorGtin.MotivoNoValido("0000000000000"));
            Assert.AreEqual("El dígito de control no cuadra (debería ser 6)", ValidadorGtin.MotivoNoValido(EAN13_CONTROL_MAL));
        }

        [TestMethod]
        public void EsCodigoValido_EsLoMismoQueNoTenerMotivo()
        {
            foreach (string codigo in new[] { "1", "0", "123", EAN13_BUENO, EAN13_CONTROL_MAL, "96385074", "036000291452", "10012345678902", "0000000000000", "", null, "84370205121X6" })
            {
                Assert.AreEqual(ValidadorGtin.MotivoNoValido(codigo) == null, ValidadorGtin.EsCodigoValido(codigo), codigo ?? "null");
            }
        }

        [TestMethod]
        public void Candidatos_FichaYTabla_CeroUnoCientoVeintitresYControlMal()
        {
            RepositorioFalso repositorio = ConDatos();

            List<CandidatoLimpiezaCodigoBarrasDTO> candidatos = ServicioLimpiezaCodigosBarras.Candidatos(repositorio.LeerFichasConCodigo(), repositorio.LeerFilasTabla());

            CollectionAssert.AreEqual(
                new[] { "100 1 Ficha", "100 1 Tabla", "101 0 Ficha", "102 123 Ficha", "103 " + EAN13_CONTROL_MAL + " Ficha", "104 0 Tabla", "999 12 Tabla" },
                candidatos.Select(c => $"{c.Producto} {c.Codigo} {c.Donde}").ToArray());
            Assert.IsTrue(candidatos.All(c => !string.IsNullOrEmpty(c.Motivo)));
            Assert.AreEqual(1, candidatos.Single(c => c.Donde == InformeCodigosBarras.DONDE_TABLA && c.Producto == "100").Id);
            Assert.IsNull(candidatos.Single(c => c.Donde == InformeCodigosBarras.DONDE_FICHA && c.Producto == "100").Id);
        }

        [TestMethod]
        public void Candidatos_TambienLosNoActivos_ConActivoFalse()
        {
            RepositorioFalso repositorio = ConDatos();

            List<CandidatoLimpiezaCodigoBarrasDTO> candidatos = ServicioLimpiezaCodigosBarras.Candidatos(repositorio.LeerFichasConCodigo(), repositorio.LeerFilasTabla());

            Assert.IsFalse(candidatos.Single(c => c.Producto == "103").Activo, "Estado -1: no activo, pero se limpia");
            Assert.IsFalse(candidatos.Single(c => c.Producto == "999").Activo, "Sin ficha: no activo");
            Assert.IsTrue(candidatos.Single(c => c.Producto == "102").Activo);
        }

        [TestMethod]
        public void Candidatos_NoTocaElEan13BuenoNiElInternoDel29NiLosVacios()
        {
            string interno = CodigoBarrasInterno.DeReferencia("17404");
            Assert.AreEqual("2900000174047", interno);
            Assert.IsTrue(CodigoBarrasInterno.EsInterno(interno));
            var fichas = new[] { Ficha("104", EAN13_BUENO), Ficha("105", "   "), Ficha("106", null), Ficha("17404", interno), Ficha("17405", CodigoBarrasInterno.DeContador(7)) };
            var filas = new[] { Fila(2, "104", EAN13_BUENO), Fila(4, "17404", interno), Fila(6, "17405", CodigoBarrasInterno.DeContador(7)) };

            List<CandidatoLimpiezaCodigoBarrasDTO> candidatos = ServicioLimpiezaCodigosBarras.Candidatos(fichas, filas);

            Assert.AreEqual(0, candidatos.Count, string.Join(", ", candidatos.Select(c => c.Codigo)));
        }

        [TestMethod]
        public void Ejecutar_Mirar_NoTocaNada()
        {
            IRepositorioLimpiezaCodigosBarras repositorio = A.Fake<IRepositorioLimpiezaCodigosBarras>();
            _ = A.CallTo(() => repositorio.LeerFichasConCodigo()).Returns(new List<FichaCodigoBarrasLimpieza> { Ficha("100", "1") });
            _ = A.CallTo(() => repositorio.LeerFilasTabla()).Returns(new List<FilaCodigoBarrasLimpieza> { Fila(1, "100", "1") });

            ResultadoLimpiezaCodigosBarrasDTO resultado = new ServicioLimpiezaCodigosBarras(repositorio).Ejecutar(false, "Carlos");

            Assert.IsFalse(resultado.Aplicar);
            Assert.AreEqual(2, resultado.Candidatos.Count);
            Assert.AreEqual(0, resultado.Limpiados);
            StringAssert.Contains(resultado.Mensaje, "Modo mirar");
            A.CallTo(() => repositorio.Limpiar(A<IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO>>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Ejecutar_Aplicar_LimpiaYDejaCadaCodigoEnModificaciones()
        {
            RepositorioFalso repositorio = ConDatos();

            ResultadoLimpiezaCodigosBarrasDTO resultado = new ServicioLimpiezaCodigosBarras(repositorio).Ejecutar(true, "NUEVAVISION\\Carlos");

            Assert.IsTrue(resultado.Aplicar);
            Assert.AreEqual(7, resultado.Candidatos.Count);
            Assert.AreEqual(7, resultado.Limpiados);
            Assert.AreEqual(0, resultado.Errores.Count);
            Assert.IsNull(repositorio.Fichas.Single(f => f.Producto == "100").CodBarras, "La ficha queda a NULL, como los productos sin código");
            Assert.AreEqual(EAN13_BUENO, repositorio.Fichas.Single(f => f.Producto == "104").CodBarras);
            CollectionAssert.AreEquivalent(new[] { 2, 4 }, repositorio.Filas.Select(f => f.Id).ToArray());
            Assert.AreEqual(7, repositorio.Modificaciones.Count);
            Modificacion ficha = repositorio.Modificaciones.First(m => m.Tabla == "Productos");
            Assert.AreEqual("Producto 1/100 CodBarras=1", ficha.Anterior);
            StringAssert.StartsWith(ficha.Nuevo, "Producto 1/100 CodBarras= (NestoAPI#776");
            StringAssert.Contains(ficha.Nuevo, "usuario NUEVAVISION\\Carlos");
            Assert.AreEqual("NUEVAVISION\\Carlos", ficha.Usuario);
            Modificacion tabla = repositorio.Modificaciones.First(m => m.Tabla == "ProductosCodigosBarras");
            Assert.AreEqual("Producto 1/100 Id=1 Codigo=1", tabla.Anterior);
            StringAssert.Contains(tabla.Nuevo, "(fila borrada)");
            StringAssert.StartsWith(resultado.Mensaje, "Limpiados 7 de 7");
        }

        [TestMethod]
        public void Ejecutar_Aplicar_EsIdempotente()
        {
            RepositorioFalso repositorio = ConDatos();
            var servicio = new ServicioLimpiezaCodigosBarras(repositorio);
            _ = servicio.Ejecutar(true, "Carlos");

            ResultadoLimpiezaCodigosBarrasDTO segunda = servicio.Ejecutar(true, "Carlos");

            Assert.AreEqual(0, segunda.Candidatos.Count);
            Assert.AreEqual(0, segunda.Limpiados);
            Assert.AreEqual(7, repositorio.Modificaciones.Count, "La segunda pasada no audita nada");
            Assert.AreEqual(1, repositorio.Llamadas, "Ni llega a abrir transacción");
        }

        [TestMethod]
        public void Ejecutar_Aplicar_ElCodigoQueHaCambiadoMientrasTanto_NoSeTocaNiSeCuenta()
        {
            RepositorioFalso repositorio = ConDatos();
            IRepositorioLimpiezaCodigosBarras envoltorio = A.Fake<IRepositorioLimpiezaCodigosBarras>();
            _ = A.CallTo(() => envoltorio.LeerFichasConCodigo()).ReturnsLazily(() => repositorio.LeerFichasConCodigo());
            _ = A.CallTo(() => envoltorio.LeerFilasTabla()).ReturnsLazily(() => repositorio.LeerFilasTabla());
            _ = A.CallTo(() => envoltorio.Limpiar(A<IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO>>._, A<string>._))
                .ReturnsLazily((IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO> lote, string usuario) =>
                {
                    repositorio.Fichas.Single(f => f.Producto == "102").CodBarras = EAN13_BUENO; // alguien lo ha corregido
                    return repositorio.Limpiar(lote, usuario);
                });

            ResultadoLimpiezaCodigosBarrasDTO resultado = new ServicioLimpiezaCodigosBarras(envoltorio).Ejecutar(true, "Carlos");

            Assert.AreEqual(6, resultado.Limpiados);
            Assert.AreEqual(EAN13_BUENO, repositorio.Fichas.Single(f => f.Producto == "102").CodBarras);
            StringAssert.Contains(resultado.Mensaje, "habían cambiado");
        }

        [TestMethod]
        public void Ejecutar_Aplicar_SiFallaUnProducto_SigueConLosDemasYLoAnotaEnErrores()
        {
            RepositorioFalso repositorio = ConDatos();
            IRepositorioLimpiezaCodigosBarras envoltorio = A.Fake<IRepositorioLimpiezaCodigosBarras>();
            _ = A.CallTo(() => envoltorio.LeerFichasConCodigo()).ReturnsLazily(() => repositorio.LeerFichasConCodigo());
            _ = A.CallTo(() => envoltorio.LeerFilasTabla()).ReturnsLazily(() => repositorio.LeerFilasTabla());
            _ = A.CallTo(() => envoltorio.Limpiar(A<IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO>>._, A<string>._))
                .ReturnsLazily((IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO> lote, string usuario) =>
                {
                    if (lote.Any(c => c.Producto == "102"))
                    {
                        throw new InvalidOperationException("El trigger no deja");
                    }
                    return repositorio.Limpiar(lote, usuario);
                });

            ResultadoLimpiezaCodigosBarrasDTO resultado = new ServicioLimpiezaCodigosBarras(envoltorio).Ejecutar(true, "Carlos");

            Assert.AreEqual(6, resultado.Limpiados);
            Assert.AreEqual(1, resultado.Errores.Count);
            StringAssert.Contains(resultado.Errores[0], "Producto 102");
            StringAssert.Contains(resultado.Errores[0], "El trigger no deja");
            Assert.AreEqual("123", repositorio.Fichas.Single(f => f.Producto == "102").CodBarras);
            Assert.IsNull(repositorio.Fichas.Single(f => f.Producto == "100").CodBarras);
            StringAssert.Contains(resultado.Mensaje, "vea Errores");
        }

        [TestMethod]
        public void Ejecutar_Aplicar_LotesDeProductos_LaFichaYSuTablaVanEnElMismoLote()
        {
            var fichas = Enumerable.Range(1, ServicioLimpiezaCodigosBarras.PRODUCTOS_POR_LOTE + 1).Select(i => Ficha(i.ToString("D5"), "1")).ToList();
            var filas = fichas.Select((f, i) => Fila(i + 1, f.Producto, "1")).ToList();
            IRepositorioLimpiezaCodigosBarras repositorio = A.Fake<IRepositorioLimpiezaCodigosBarras>();
            _ = A.CallTo(() => repositorio.LeerFichasConCodigo()).Returns(fichas);
            _ = A.CallTo(() => repositorio.LeerFilasTabla()).Returns(filas);
            var lotes = new List<List<CandidatoLimpiezaCodigoBarrasDTO>>();
            _ = A.CallTo(() => repositorio.Limpiar(A<IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO>>._, A<string>._))
                .ReturnsLazily((IReadOnlyList<CandidatoLimpiezaCodigoBarrasDTO> lote, string usuario) => { lotes.Add(lote.ToList()); return lote.Count; });

            ResultadoLimpiezaCodigosBarrasDTO resultado = new ServicioLimpiezaCodigosBarras(repositorio).Ejecutar(true, "Carlos");

            Assert.AreEqual(2, lotes.Count);
            Assert.AreEqual(ServicioLimpiezaCodigosBarras.PRODUCTOS_POR_LOTE * 2, lotes[0].Count);
            CollectionAssert.AreEqual(new[] { "Ficha", "Tabla" }, lotes[1].Select(c => c.Donde).ToArray(), "La ficha antes que su copia de la tabla");
            Assert.AreEqual(fichas.Count * 2, resultado.Limpiados);
        }

        [TestMethod]
        public void Auditoria_UsuarioLargo_SeRecortaA30()
        {
            Modificacion m = ServicioLimpiezaCodigosBarras.Auditoria(
                new CandidatoLimpiezaCodigoBarrasDTO { Empresa = "1", Producto = "100", Codigo = "1", Donde = "Ficha", Motivo = "x" },
                new string('a', 40));

            Assert.AreEqual(30, m.Usuario.Length);
            StringAssert.Contains(m.Nuevo, new string('a', 40));
        }

        [TestMethod]
        public void Sql_EnBlancoEsNullYSeAuditaEnModificacionesConElUsuario()
        {
            StringAssert.Contains(RepositorioLimpiezaCodigosBarrasSql.SQL_LIMPIAR_FICHA, "CodBarras = NULL");
            StringAssert.Contains(RepositorioLimpiezaCodigosBarrasSql.SQL_LIMPIAR_FICHA, "RTRIM(CodBarras) = @codigo");
            StringAssert.Contains(RepositorioLimpiezaCodigosBarrasSql.SQL_BORRAR_FILA, "DELETE FROM ProductosCodigosBarras WHERE Id = @id");
            StringAssert.Contains(RepositorioLimpiezaCodigosBarrasSql.SQL_AUDITORIA, "INSERT INTO Modificaciones (Tabla, Anterior, Nuevo, Usuario");
            Assert.IsFalse(RepositorioLimpiezaCodigosBarrasSql.SQL_FICHAS.Contains("Estado >="), "Todos los productos, también los no activos");
        }

        [TestMethod]
        public void PuedeLimpiar_SoloDireccionInformaticaYCompras()
        {
            Assert.IsTrue(ServicioLimpiezaCodigosBarras.PuedeLimpiar(ServicioCodigosBarrasTests.ConRol("Dirección")));
            Assert.IsTrue(ServicioLimpiezaCodigosBarras.PuedeLimpiar(ServicioCodigosBarrasTests.ConRol("NUEVAVISION\\Informática")));
            Assert.IsTrue(ServicioLimpiezaCodigosBarras.PuedeLimpiar(ServicioCodigosBarrasTests.ConRol("Compras")));
            Assert.IsFalse(ServicioLimpiezaCodigosBarras.PuedeLimpiar(ServicioCodigosBarrasTests.ConRol("Almacén")));
            Assert.IsFalse(ServicioLimpiezaCodigosBarras.PuedeLimpiar(ServicioCodigosBarrasTests.ConRol("Tiendas")));
            Assert.IsFalse(ServicioLimpiezaCodigosBarras.PuedeLimpiar(null));
        }

        private static ProductosCodigosBarrasController Controlador(string rol, ServicioLimpiezaCodigosBarras limpieza)
        {
            var controller = new ProductosCodigosBarrasController(A.Fake<IServicioCodigosBarras>(), null, limpieza)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.RequestContext.Principal = ServicioCodigosBarrasTests.ConRol(rol);
            return controller;
        }

        [TestMethod]
        public void Controller_Almacen_403YNoLeeNada()
        {
            IRepositorioLimpiezaCodigosBarras repositorio = A.Fake<IRepositorioLimpiezaCodigosBarras>();

            IHttpActionResult resultado = Controlador("Almacén", new ServicioLimpiezaCodigosBarras(repositorio)).PostLimpiarNoValidos(true);

            Assert.IsInstanceOfType(resultado, typeof(ResponseMessageResult));
            Assert.AreEqual(HttpStatusCode.Forbidden, ((ResponseMessageResult)resultado).Response.StatusCode);
            A.CallTo(() => repositorio.LeerFichasConCodigo()).MustNotHaveHappened();
        }

        [TestMethod]
        public void Controller_Compras_PorDefectoMira()
        {
            RepositorioFalso repositorio = ConDatos();

            IHttpActionResult resultado = Controlador("Compras", new ServicioLimpiezaCodigosBarras(repositorio)).PostLimpiarNoValidos();

            var ok = resultado as OkNegotiatedContentResult<ResultadoLimpiezaCodigosBarrasDTO>;
            Assert.IsNotNull(ok);
            Assert.IsFalse(ok.Content.Aplicar);
            Assert.AreEqual(7, ok.Content.Candidatos.Count);
            Assert.AreEqual(0, repositorio.Llamadas);
            Assert.AreEqual("1", repositorio.Fichas.Single(f => f.Producto == "100").CodBarras);
        }

        [TestMethod]
        public void Controller_SinServicio_404()
        {
            Assert.IsInstanceOfType(Controlador("Dirección", null).PostLimpiarNoValidos(), typeof(NotFoundResult));
        }

        [TestMethod]
        public void Candidato_ElIdNoViajaEnElJson()
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(new CandidatoLimpiezaCodigoBarrasDTO { Producto = "100", Codigo = "1", Id = 7 });

            Assert.IsFalse(json.Contains("\"Id\""), json);
        }
    }
}
