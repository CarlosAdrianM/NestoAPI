using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Infraestructure.Productos;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Productos
{
    /// <summary>NestoAPI#674: el dígito de control GTIN (EAN-8, UPC-A, EAN-13, ITF-14).</summary>
    [TestClass]
    public class ValidadorGtinTests
    {
        [TestMethod]
        public void EsCodigoValido_Ean13Reales_Validos()
        {
            Assert.IsTrue(ValidadorGtin.EsCodigoValido("8437020512176"));
            Assert.IsTrue(ValidadorGtin.EsCodigoValido("8437020512237"));
            Assert.IsTrue(ValidadorGtin.EsCodigoValido(" 8437020512176 "), "Los espacios alrededor no cuentan");
        }

        [TestMethod]
        public void EsCodigoValido_Ean13ConUnaCifraCambiada_NoValido()
        {
            Assert.IsFalse(ValidadorGtin.EsCodigoValido("8437020512177"), "Dígito de control cambiado");
            Assert.IsFalse(ValidadorGtin.EsCodigoValido("8437020512276"), "Una cifra del medio cambiada");
        }

        [TestMethod]
        public void EsCodigoValido_Ean8()
        {
            Assert.IsTrue(ValidadorGtin.EsCodigoValido("96385074"));
            Assert.IsFalse(ValidadorGtin.EsCodigoValido("96385075"));
        }

        [TestMethod]
        public void EsCodigoValido_UpcA()
        {
            Assert.IsTrue(ValidadorGtin.EsCodigoValido("036000291452"));
            Assert.IsFalse(ValidadorGtin.EsCodigoValido("036000291453"));
        }

        [TestMethod]
        public void EsCodigoValido_Itf14()
        {
            Assert.IsTrue(ValidadorGtin.EsCodigoValido("18437020512173"));
            Assert.IsFalse(ValidadorGtin.EsCodigoValido("18437020512174"));
        }

        [TestMethod]
        public void EsCodigoValido_RellenosLetrasYLongitudesRaras_NoValidos()
        {
            foreach (string codigo in new[] { "1", "0", null, "", "   ", "44724", "84370205121", "843702051217A", "ABCDEFGHIJKLM",
                "0000000000000", "00000000", "843702051217601", "8437020-12176" })
            {
                Assert.IsFalse(ValidadorGtin.EsCodigoValido(codigo), $"«{codigo}» no debería ser válido");
            }
        }

        [TestMethod]
        public void SoloSiValido_DevuelveElCodigoRecortadoONull()
        {
            Assert.AreEqual("8437020512176", ValidadorGtin.SoloSiValido(" 8437020512176 "));
            Assert.IsNull(ValidadorGtin.SoloSiValido("1"));
            Assert.IsNull(ValidadorGtin.SoloSiValido(null));
        }

        [TestMethod]
        public void MensajeNoValido_DeUsted()
        {
            Assert.AreEqual("El código 1 no es un código de barras válido: compruebe las cifras.", ValidadorGtin.MensajeNoValido(" 1 "));
        }
    }

    /// <summary>NestoAPI#674: al leer, un código guardado que no es GTIN no casa; al guardar, se rechaza.</summary>
    [TestClass]
    public class CodigosBarrasGtinTests
    {
        private RepositorioCodigosBarrasFalso repositorio;
        private ServicioCodigosBarras servicio;

        [TestInitialize]
        public void Setup()
        {
            repositorio = new RepositorioCodigosBarrasFalso();
            servicio = new ServicioCodigosBarras(repositorio);
            repositorio.Producto("44724", "1", "PRODUCTO CON RELLENO EN LA FICHA");
            repositorio.Producto("44725", "8437020512176", "PRODUCTO CON EAN");
        }

        [TestMethod]
        public async Task BuscarPorCodigo_CodigoGuardadoNoValido_NoCasa()
        {
            List<ProductoPorCodigoBarrasDTO> encontrados = await servicio.BuscarPorCodigo("1", "1");

            Assert.AreEqual(0, encontrados.Count);
        }

        [TestMethod]
        public async Task BuscarPorCodigo_CodigoValido_SigueCasando()
        {
            List<ProductoPorCodigoBarrasDTO> encontrados = await servicio.BuscarPorCodigo("1", "8437020512176");

            Assert.AreEqual("44725", encontrados.Single().Producto);
        }

        [TestMethod]
        public async Task Anadir_CodigoNoValido_NoValidoConElMensajeYNoGuarda()
        {
            ResultadoCodigoBarras resultado = await servicio.Anadir("44725", new NuevoCodigoBarrasDTO { Empresa = "1", Codigo = "8437020512177" }, "Alfredo");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, resultado.Estado);
            Assert.AreEqual("El código 8437020512177 no es un código de barras válido: compruebe las cifras.", resultado.Mensaje);
            Assert.AreEqual(0, repositorio.Guardados);
        }

        [TestMethod]
        public async Task MarcarPrincipal_CodigoNoValidoDeAntes_NoValidoYNoTocaLaFicha()
        {
            ProductoCodigoBarras relleno = repositorio.Fila("44725", "0");

            ResultadoCodigoBarras resultado = await servicio.MarcarPrincipal("1", "44725", relleno.Id, "Alfredo");

            Assert.AreEqual(EstadoOperacionCodigoBarras.NoValido, resultado.Estado);
            Assert.AreEqual("8437020512176", repositorio.Productos.Single(p => p.Número == "44725").CodBarras);
            Assert.AreEqual(0, repositorio.Guardados);
        }

        [TestMethod]
        public async Task PostCodigoBarras_CodigoNoValido_400ConElMensaje()
        {
            var controller = new ProductosCodigosBarrasController(servicio)
            {
                Request = new HttpRequestMessage(),
                Configuration = new HttpConfiguration()
            };
            controller.RequestContext.Principal = ServicioCodigosBarrasTests.ConRol("Compras");

            IHttpActionResult resultado = await controller.PostCodigoBarras("44725", new NuevoCodigoBarrasDTO { Codigo = "1" });

            var error = resultado as BadRequestErrorMessageResult;
            Assert.IsNotNull(error, "Un código que no es GTIN da 400");
            Assert.AreEqual("El código 1 no es un código de barras válido: compruebe las cifras.", error.Message);
        }

        [TestMethod]
        public void CompletadorCodigosBarras_Juntar_QuitaLosNoValidos()
        {
            List<string> codigos = CompletadorCodigosBarras.Juntar("1", new[] { "8437020512176", "0", "ABC" });

            CollectionAssert.AreEqual(new[] { "8437020512176" }, codigos);
        }

        [TestMethod]
        public void CasadorEscaneos_CodigoNoValidoEnLaLinea_NoCasaYCuentaComoSinCodigo()
        {
            var lineas = new List<LineaPickingAlmacenDTO>
            {
                new LineaPickingAlmacenDTO { Producto = "44724", CodigoBarras = "1", Cantidad = 1 },
                new LineaPickingAlmacenDTO { Producto = "44726", CodigoBarras = "1", Cantidad = 1 }
            };

            List<LineaPickingAlmacenDTO> recorrido = CasadorEscaneos.OrdenarRecorrido(lineas);

            Assert.IsTrue(recorrido.All(l => l.SinCodigo && l.CodigoBarras == null && !l.CodigoDuplicado));
            Assert.AreEqual(CasadorEscaneos.TipoResolucion.Ajeno, CasadorEscaneos.ResolverCodigo("1",
                new[] { new KeyValuePair<string, string>("44724", "1") }).Tipo);
        }
    }

    /// <summary>NestoAPI#674: el informe semanal a Compras.</summary>
    [TestClass]
    public class InformeCodigosBarrasTests
    {
        private static ProductoActivoCodigoBarras P(string numero, string codigo, bool ficticio = false)
            => new ProductoActivoCodigoBarras { Producto = numero, Nombre = "NOMBRE " + numero, CodBarras = codigo, Ficticio = ficticio };

        [TestMethod]
        public void Construir_ClasificaSinCodigoNoValidosYCompartidos()
        {
            var productos = new[]
            {
                P("1001", null),                    // sin código
                P("1002", "1"),                     // solo uno no válido
                P("1003", "8437020512176"),         // compartido con 1004 (tabla)
                P("1004", "8437020512237"),
                P("1005", null, ficticio: true),    // ficticio: no cuenta como sin código
                P("1006", "0")                      // no válido en la ficha, pero tiene uno válido en la tabla
            };
            var tabla = new[]
            {
                new CodigoActivoTabla { Producto = "1004", Codigo = "8437020512176" },
                new CodigoActivoTabla { Producto = "1006", Codigo = "96385074" }
            };

            InformeCodigosBarras informe = InformeCodigosBarras.Construir(productos, tabla, new DateTime(2026, 10, 19));

            Assert.AreEqual(6, informe.ProductosActivos);
            CollectionAssert.AreEqual(new[] { "1001" }, informe.SinCodigo.Select(p => p.Producto).ToList());
            CollectionAssert.AreEqual(new[] { "1002:1:True", "1006:0:False" },
                informe.CodigosNoValidos.Select(c => $"{c.Producto}:{c.Codigo}:{c.SinNingunoValido}").ToList());
            Assert.AreEqual(1, informe.SoloConNoValidos);
            Assert.AreEqual("8437020512176", informe.Compartidos.Single().Codigo);
            CollectionAssert.AreEqual(new[] { "1003", "1004" }, informe.Compartidos.Single().Productos.Select(p => p.Producto).ToList());
            Assert.AreEqual(0, informe.NoSeLeen.Count);
            Assert.AreEqual(0, informe.Tecleadas.Count);
        }

        [TestMethod]
        public void Construir_Ariadna43_NoSeLeenYTecleadosPorUsuario()
        {
            var productos = new[] { P("1001", null), P("1003", "8437020512176"), P("1004", "8437020512237") };
            var motivos = new[]
            {
                new MotivoTecleoGuardado { Producto = "1003", Motivo = "NOSELEE", Codigo = "8437020512176", Veces = 1, Usuario = "Andre", FechaModificacion = new DateTime(2026, 10, 15) },
                new MotivoTecleoGuardado { Producto = "1004", Motivo = "SINCODIGO", Veces = 1, Usuario = "Andre", FechaModificacion = new DateTime(2026, 10, 16) }
            };
            var tecleadas = new[]
            {
                new EntradaTecleadaInforme { Producto = "1003", Usuario = "Andre", Veces = 3, Unidades = 10 },
                new EntradaTecleadaInforme { Producto = "1001", Usuario = "Andre", Veces = 2, Unidades = 2 },
                new EntradaTecleadaInforme { Producto = "1004", Usuario = "Alfredo", Veces = 1, Unidades = 6 }
            };

            InformeCodigosBarras informe = InformeCodigosBarras.Construir(productos, new CodigoActivoTabla[0], new DateTime(2026, 10, 19), motivos, tecleadas);

            CodigoQueNoSeLeeInforme noSeLee = informe.NoSeLeen.Single();
            Assert.AreEqual("1003", noSeLee.Producto);
            Assert.AreEqual("NOMBRE 1003", noSeLee.Nombre);
            Assert.AreEqual(3, noSeLee.TecleadasSemana);
            Assert.AreEqual("Andre", noSeLee.Usuario);

            EntradaTecleadaInforme sinCodigo = informe.Tecleadas.Single(t => t.Producto == "1001");
            Assert.IsFalse(sinCodigo.TieneCodigoValido);
            Assert.IsNull(sinCodigo.Motivo);
            Assert.AreEqual("No se lee", informe.Tecleadas.Single(t => t.Producto == "1003").Motivo);
            Assert.AreEqual("No lleva código", informe.Tecleadas.Single(t => t.Producto == "1004").Motivo);

            TecleadoPorUsuarioInforme andre = informe.TecleadasPorUsuario.First();
            Assert.AreEqual(("Andre", 5, 12, 2, 3), (andre.Usuario, andre.Entradas, andre.Unidades, andre.Productos, andre.EntradasConCodigo));
            Assert.AreEqual("Alfredo", informe.TecleadasPorUsuario.Last().Usuario);
        }

        [TestMethod]
        public void GenerarHtml_Ariadna43_ConTecleadosSacaLaTablaPorUsuario()
        {
            InformeCodigosBarras informe = InformeCodigosBarras.Construir(new[] { P("1003", "8437020512176") }, new CodigoActivoTabla[0], new DateTime(2026, 10, 19),
                new[] { new MotivoTecleoGuardado { Producto = "1003", Motivo = "NOSELEE", Codigo = "8437020512176", Veces = 1, Usuario = "Andre", FechaModificacion = new DateTime(2026, 10, 15) } },
                new[] { new EntradaTecleadaInforme { Producto = "1003", Usuario = "Andre", Veces = 3, Unidades = 10 } });

            string html = InformeCodigosBarrasJobsService.GenerarHtml(informe);

            StringAssert.Contains(html, "<td>Andre</td><td>3</td><td>10</td><td>1</td><td>3</td>");
            StringAssert.Contains(html, "8437020512176");
            Assert.IsTrue(InformeCodigosBarrasJobsService.GenerarExcel(informe).Length > 0);
        }

        [TestMethod]
        public void SqlTecleadas_Ariadna43_SoloTeclaYSinLosDeshacer()
        {
            StringAssert.Contains(RepositorioInformeCodigosBarrasSql.SQL_TECLEADAS, "e.Metodo = 'TECLA' AND e.Cantidad > 0");
            StringAssert.Contains(RepositorioInformeCodigosBarrasSql.SQL_MOTIVOS_TECLEO, "IF OBJECT_ID('dbo.ProductosMotivoTecleo') IS NOT NULL");
        }

        [TestMethod]
        public void Procesar_MandaElCorreoACompras()
        {
            IRepositorioInformeCodigosBarras repositorio = A.Fake<IRepositorioInformeCodigosBarras>();
            A.CallTo(() => repositorio.LeerProductosActivos("1")).Returns(new List<ProductoActivoCodigoBarras> { P("1001", null), P("1002", "1") });
            A.CallTo(() => repositorio.LeerCodigosActivosTabla("1")).Returns(new List<CodigoActivoTabla>());
            A.CallTo(() => repositorio.LeerMotivosTecleo("1")).Returns(new List<MotivoTecleoGuardado>());
            A.CallTo(() => repositorio.LeerTecleadas("1", new DateTime(2026, 10, 12))).Returns(new List<EntradaTecleadaInforme>());
            IServicioCorreoElectronico correo = A.Fake<IServicioCorreoElectronico>();
            MailMessage enviado = null;
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).Invokes((MailMessage m) =>
            {
                enviado = m;
                Assert.AreEqual(Constantes.Correos.COMPRAS, m.To.Single().Address);
                StringAssert.Contains(m.Subject, "1 sin código");
                StringAssert.Contains(m.Body, "Códigos que no se leen");
                StringAssert.Contains(m.Body, "Productos tecleados en el almacén");
                Assert.AreEqual(1, m.Attachments.Count);
            }).Returns(true);

            bool mandado = InformeCodigosBarrasJobsService.Procesar(new DependenciasInformeCodigosBarras
            {
                Repositorio = repositorio,
                Correo = correo,
                Ahora = new DateTime(2026, 10, 19, 8, 10, 0)
            });

            Assert.IsTrue(mandado);
            Assert.IsNotNull(enviado);
            A.CallTo(() => repositorio.LeerTecleadas("1", new DateTime(2026, 10, 12))).MustHaveHappenedOnceExactly();
        }
    }
}
