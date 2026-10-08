using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.OpenAI;
using NestoAPI.Infraestructure.Pagos;
using NestoAPI.Models.Pagos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Pagos
{
    /// <summary>NestoAPI#609: revisar el concepto de un enlace de pago (propuesta; el usuario confirma).</summary>
    [TestClass]
    public class RevisorConceptoTests
    {
        #region Reglas deterministas

        [TestMethod]
        public void AplicarDeterminista_SerieDeFacturaEnMinusculas_LaPasaAMayusculas()
        {
            Assert.AreEqual("Pago factura NV2613646", ReglasRevisionConcepto.AplicarDeterminista("Pago factura nv2613646"));
        }

        [TestMethod]
        public void AplicarDeterminista_TodasLasSeriesReales_EnMayusculas()
        {
            foreach (string serie in new[] { "nv", "gb", "cv", "rv", "ev", "dv", "ul", "rc" })
            {
                Assert.AreEqual($"Pago {serie.ToUpperInvariant()}2600001", ReglasRevisionConcepto.AplicarDeterminista($"Pago {serie}2600001"));
            }
        }

        [TestMethod]
        public void AplicarDeterminista_PalabraQueEmpiezaPorSerieSinCifras_NoLaToca()
        {
            Assert.AreEqual("Curso nvidia y cvc 2600001", ReglasRevisionConcepto.AplicarDeterminista("Curso nvidia y cvc 2600001"));
        }

        [TestMethod]
        public void AplicarDeterminista_TodoMinusculas_TipoOracionYSerie()
        {
            Assert.AreEqual("Pago 2/2 NV2613646", ReglasRevisionConcepto.AplicarDeterminista("pago 2/2 nv2613646"));
        }

        [TestMethod]
        public void TokensConCifras_IgnoraPuntuacionDelBorde()
        {
            CollectionAssert.AreEqual(new[] { "29/09", "926465", "41235" },
                ReglasRevisionConcepto.TokensConCifras("Master classn 29/09, pedido 926465 (cliente 41235)."));
        }

        [TestMethod]
        public void RespetaNumeros_FechaCambiada_False()
        {
            Assert.IsFalse(ReglasRevisionConcepto.RespetaNumeros("Masterclass 29/09", "Masterclass 29/10"));
        }

        [TestMethod]
        public void RespetaNumeros_NumeroQuitado_False()
        {
            Assert.IsFalse(ReglasRevisionConcepto.RespetaNumeros("Pago pedido 925240 de Nueva Visión", "Pago pedido de Nueva Visión"));
        }

        [TestMethod]
        public void RespetaNumeros_NumeroAnadido_False()
        {
            Assert.IsFalse(ReglasRevisionConcepto.RespetaNumeros("Masterclass", "Masterclass 2026"));
        }

        [TestMethod]
        public void RespetaNumeros_SerieSoloCambiaDeMayusculas_True()
        {
            Assert.IsTrue(ReglasRevisionConcepto.RespetaNumeros("Pago factura nv2613646", "Pago factura NV2613646"));
        }

        [TestMethod]
        public void PropuestaValida_MasLargaQueElLimiteDeRedsys_False()
        {
            string larga = "Curso " + new string('a', ReglasRevisionConcepto.LONGITUD_MAXIMA);
            Assert.IsFalse(ReglasRevisionConcepto.PropuestaValida("Curso", larga));
        }

        [TestMethod]
        public void PropuestaValida_AnadeMuchasPalabras_False()
        {
            Assert.IsFalse(ReglasRevisionConcepto.PropuestaValida("Masterclass 29/09", "Masterclass de cloasma y léntigos solares 29/09"));
        }

        [TestMethod]
        public void LimpiarRespuestaIA_QuitaComillasYPrefijo()
        {
            Assert.AreEqual("Formación protocolos depilación", ReglasRevisionConcepto.LimpiarRespuestaIA("Concepto: «Formación protocolos depilación»\n"));
            Assert.AreEqual("Hola", ReglasRevisionConcepto.LimpiarRespuestaIA("\"Hola\""));
            Assert.IsNull(ReglasRevisionConcepto.LimpiarRespuestaIA("   "));
        }

        [TestMethod]
        public void CalcularCambios_AgrupaPalabrasConsecutivas()
        {
            List<CambioConcepto> cambios = ReglasRevisionConcepto.CalcularCambios(
                "Primer pago curso micronileng Yolanda", "Primer pago curso Microneedling Yolanda");
            Assert.AreEqual(1, cambios.Count);
            Assert.AreEqual("micronileng", cambios[0].De);
            Assert.AreEqual("Microneedling", cambios[0].A);

            cambios = ReglasRevisionConcepto.CalcularCambios("Master classn 29/09", "Masterclass 29/09");
            Assert.AreEqual(1, cambios.Count);
            Assert.AreEqual("Master classn", cambios[0].De);
            Assert.AreEqual("Masterclass", cambios[0].A);
        }

        [TestMethod]
        public void CalcularCambios_VariosCambiosSeparados()
        {
            List<CambioConcepto> cambios = ReglasRevisionConcepto.CalcularCambios(
                "Formación protocoloes depilación 19/10/26 reserva formacion", "Formación protocolos depilación 19/10/26 reserva formación");
            Assert.AreEqual(2, cambios.Count);
            Assert.AreEqual("protocoloes", cambios[0].De);
            Assert.AreEqual("protocolos", cambios[0].A);
            Assert.AreEqual("formacion", cambios[1].De);
            Assert.AreEqual("formación", cambios[1].A);
        }

        [TestMethod]
        public void CalcularCambios_SinCambios_ListaVacia()
        {
            Assert.AreEqual(0, ReglasRevisionConcepto.CalcularCambios("Masterclass 29/09", "Masterclass 29/09").Count);
        }

        [TestMethod]
        public void ParsearGlosario_VariasFilasYSeparadores_SinDuplicados()
        {
            List<string> terminos = ReglasRevisionConcepto.ParsearGlosario(new[] { "Masterclass, Microneedling ,Cloasma   ", "Léntigos;PDRN\nmasterclass", null });
            CollectionAssert.AreEqual(new[] { "Masterclass", "Microneedling", "Cloasma", "Léntigos", "PDRN" }, terminos);
        }

        [TestMethod]
        public void PalabrasParaBuscarProductos_QuitaTramiteCifrasYCortas_Maximo3()
        {
            CollectionAssert.AreEqual(new[] { "micronileng", "Yolanda" },
                ReglasRevisionConcepto.PalabrasParaBuscarProductos("Primer pago curso micronileng Yolanda 29/09"));
            CollectionAssert.AreEqual(new[] { "Cloasma", "lentigos", "gestoon" },
                ReglasRevisionConcepto.PalabrasParaBuscarProductos("Cloasma,lentigos, gestoon con Yolanda bonificables"));
        }

        [TestMethod]
        public void MensajeUsuario_LlevaGlosarioYConcepto()
        {
            string mensaje = ReglasRevisionConcepto.MensajeUsuario("Masterclass nad", new[] { "NAD", "PDRN" });
            StringAssert.Contains(mensaje, "Glosario: NAD; PDRN");
            StringAssert.Contains(mensaje, "Concepto: Masterclass nad");
        }

        #endregion

        #region Revisor (IA, guardas, timeout, caché)

        private static RevisorConcepto Revisor(IServicioOpenAI openAI, IFuenteGlosarioConcepto glosario = null, int milisegundos = 3000)
        {
            glosario = glosario ?? A.Fake<IFuenteGlosarioConcepto>();
            return new RevisorConcepto(glosario, () => openAI, TimeSpan.FromMilliseconds(milisegundos));
        }

        private static void IAResponde(IServicioOpenAI openAI, string respuesta)
        {
            _ = A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Returns(Task.FromResult(respuesta));
        }

        [TestMethod]
        public async Task Revisar_IAProponeCorreccion_HayCambiosYCambiosPalabraAPalabra()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Primer pago curso Microneedling Yolanda");

            RespuestaRevisarConcepto r = await Revisor(openAI).Revisar(new SolicitudRevisarConcepto { Concepto = "Primer pago curso micronileng Yolanda", Empresa = "1" });

            Assert.AreEqual("Primer pago curso micronileng Yolanda", r.Original);
            Assert.AreEqual("Primer pago curso Microneedling Yolanda", r.Propuesto);
            Assert.IsTrue(r.HayCambios);
            Assert.AreEqual(1, r.Cambios.Count);
            Assert.AreEqual("micronileng", r.Cambios[0].De);
            Assert.AreEqual("Microneedling", r.Cambios[0].A);
        }

        [TestMethod]
        public async Task Revisar_IACambiaUnaFecha_SeDescartaYQuedaLoDeterminista()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Masterclass 29/10");

            RespuestaRevisarConcepto r = await Revisor(openAI).Revisar(new SolicitudRevisarConcepto { Concepto = "Master classn 29/09" });

            Assert.AreEqual("Master classn 29/09", r.Propuesto);
            Assert.IsFalse(r.HayCambios);
            Assert.AreEqual(0, r.Cambios.Count);
        }

        [TestMethod]
        public async Task Revisar_IAQuitaElNumeroDePedido_SeDescarta()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Pago pedido de Nueva Visión");

            RespuestaRevisarConcepto r = await Revisor(openAI).Revisar(new SolicitudRevisarConcepto { Concepto = "Pago pedido 925240 de Nueva Visión" });

            Assert.AreEqual("Pago pedido 925240 de Nueva Visión", r.Propuesto);
            Assert.IsFalse(r.HayCambios);
        }

        [TestMethod]
        public async Task Revisar_IATardaMasDelTope_SoloDeterminista()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            var nunca = new TaskCompletionSource<string>();
            _ = A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Returns(nunca.Task);

            RespuestaRevisarConcepto r = await Revisor(openAI, milisegundos: 100).Revisar(new SolicitudRevisarConcepto { Concepto = "pago factura nv2613646" });

            Assert.AreEqual("Pago factura NV2613646", r.Propuesto);
            Assert.IsTrue(r.HayCambios, "La serie en mayúsculas no la hace el alta: se propone");
            Assert.AreEqual(1, r.Cambios.Count(c => c.De == "nv2613646" && c.A == "NV2613646"));
        }

        [TestMethod]
        public async Task Revisar_IALanzaExcepcion_SoloDeterminista()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            _ = A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .Throws(new InvalidOperationException("caída"));

            RespuestaRevisarConcepto r = await Revisor(openAI).Revisar(new SolicitudRevisarConcepto { Concepto = "Reserva formacion Hirsutismo" });

            Assert.AreEqual("Reserva formacion Hirsutismo", r.Propuesto);
            Assert.IsFalse(r.HayCambios);
        }

        [TestMethod]
        public async Task Revisar_IADevuelveNull_SoloDeterminista()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, null);

            RespuestaRevisarConcepto r = await Revisor(openAI).Revisar(new SolicitudRevisarConcepto { Concepto = "Reserva formacion Hirsutismo" });

            Assert.AreEqual("Reserva formacion Hirsutismo", r.Propuesto);
            Assert.IsFalse(r.HayCambios);
        }

        [TestMethod]
        public async Task Revisar_SinClaveDeOpenAI_SoloDeterminista()
        {
            RespuestaRevisarConcepto r = await Revisor(null).Revisar(new SolicitudRevisarConcepto { Concepto = "MASTERCLASS NAD" });

            Assert.AreEqual("Masterclass nad", r.Propuesto);
            Assert.IsFalse(r.HayCambios, "Las mayúsculas ya las arregla el alta: no hace falta preguntar");
            Assert.AreEqual(1, r.Cambios.Count);
        }

        [TestMethod]
        public async Task Revisar_FalloDelGlosario_SigueConLaIA()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Reserva formación Hirsutismo");
            var glosario = A.Fake<IFuenteGlosarioConcepto>();
            _ = A.CallTo(() => glosario.ObtenerTerminos(A<string>._, A<string>._)).Throws(new Exception("BD caída"));

            RespuestaRevisarConcepto r = await Revisor(openAI, glosario).Revisar(new SolicitudRevisarConcepto { Concepto = "Reserva formacion Hirsutismo" });

            Assert.AreEqual("Reserva formación Hirsutismo", r.Propuesto);
            Assert.IsTrue(r.HayCambios);
        }

        [TestMethod]
        public async Task Revisar_PasaElGlosarioALaIA()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Masterclass NAD, PDRN y exosomas 21/10");
            var glosario = A.Fake<IFuenteGlosarioConcepto>();
            _ = A.CallTo(() => glosario.ObtenerTerminos("1", "Masterclass nad, pdrn y exosomas 21/10"))
                .Returns(Task.FromResult(new List<string> { "NAD", "PDRN", "Exosomas" }));

            RespuestaRevisarConcepto r = await Revisor(openAI, glosario).Revisar(new SolicitudRevisarConcepto { Concepto = "Masterclass nad, pdrn y exosomas 21/10", Empresa = "1" });

            _ = A.CallTo(() => openAI.GenerarContenidoAsync(ReglasRevisionConcepto.PROMPT_SISTEMA,
                    A<string>.That.Contains("Glosario: NAD; PDRN; Exosomas"), A<int>._, A<double>._, A<string>._))
                .MustHaveHappenedOnceExactly();
            Assert.AreEqual("Masterclass NAD, PDRN y exosomas 21/10", r.Propuesto);
            Assert.IsTrue(r.HayCambios);
        }

        [TestMethod]
        public async Task Revisar_MismoTextoDosVeces_UnaSolaLlamadaALaIA()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Masterclass 29/09");
            RevisorConcepto revisor = Revisor(openAI);

            RespuestaRevisarConcepto r1 = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = "Master classn 29/09" });
            RespuestaRevisarConcepto r2 = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = "Master classn 29/09" });

            Assert.AreEqual("Masterclass 29/09", r1.Propuesto);
            Assert.AreEqual("Masterclass 29/09", r2.Propuesto);
            _ = A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Revisar_TrasUnTimeout_NoSeCacheaYSeReintenta()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            var nunca = new TaskCompletionSource<string>();
            A.CallTo(() => openAI.GenerarContenidoAsync(A<string>._, A<string>._, A<int>._, A<double>._, A<string>._))
                .ReturnsNextFromSequence(nunca.Task, Task.FromResult("Masterclass 29/09"));
            RevisorConcepto revisor = Revisor(openAI, milisegundos: 100);

            RespuestaRevisarConcepto r1 = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = "Master classn 29/09" });
            RespuestaRevisarConcepto r2 = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = "Master classn 29/09" });

            Assert.IsFalse(r1.HayCambios);
            Assert.AreEqual("Masterclass 29/09", r2.Propuesto);
        }

        [TestMethod]
        public async Task Revisar_ConceptoGenericoOVacio_NoLlamaALaIA()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            RevisorConcepto revisor = Revisor(openAI);

            RespuestaRevisarConcepto generico = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = FormateadorConcepto.CONCEPTO_GENERICO });
            RespuestaRevisarConcepto vacio = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = "" });
            RespuestaRevisarConcepto soloNumeros = await revisor.Revisar(new SolicitudRevisarConcepto { Concepto = "926799" });

            Assert.IsFalse(generico.HayCambios);
            Assert.IsFalse(vacio.HayCambios);
            Assert.AreEqual("", vacio.Propuesto);
            Assert.IsFalse(soloNumeros.HayCambios);
            A.CallTo(openAI).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Revisar_LaRespuestaDeLaIATraeLaSerieEnMinusculas_SeCorrige()
        {
            var openAI = A.Fake<IServicioOpenAI>();
            IAResponde(openAI, "Pago factura nv2613646");

            RespuestaRevisarConcepto r = await Revisor(openAI).Revisar(new SolicitudRevisarConcepto { Concepto = "Pago factura nv2613646" });

            Assert.AreEqual("Pago factura NV2613646", r.Propuesto);
            Assert.IsTrue(r.HayCambios);
        }

        #endregion

        #region Controlador (contrato)

        [TestMethod]
        public async Task Controlador_RevisarConcepto_DevuelveLaRespuestaDelRevisor()
        {
            var revisor = A.Fake<IRevisorConcepto>();
            var esperada = new RespuestaRevisarConcepto
            {
                Original = "Master classn 29/09",
                Propuesto = "Masterclass 29/09",
                HayCambios = true,
                Cambios = new List<CambioConcepto> { new CambioConcepto { De = "Master classn", A = "Masterclass" } }
            };
            _ = A.CallTo(() => revisor.Revisar(A<SolicitudRevisarConcepto>.That.Matches(s => s.Concepto == "Master classn 29/09" && s.Cliente == "15191")))
                .Returns(Task.FromResult(esperada));
            var controller = new PagosController(A.Fake<IServicioPagos>(), revisor);

            var resultado = await controller.RevisarConcepto(new SolicitudRevisarConcepto { Concepto = "Master classn 29/09", Empresa = "1", Cliente = "15191" })
                as OkNegotiatedContentResult<RespuestaRevisarConcepto>;

            Assert.IsNotNull(resultado);
            Assert.AreSame(esperada, resultado.Content);
        }

        [TestMethod]
        public async Task Controlador_RevisarConcepto_SinCuerpo_BadRequest()
        {
            var controller = new PagosController(A.Fake<IServicioPagos>(), A.Fake<IRevisorConcepto>());

            var resultado = await controller.RevisarConcepto(null);

            Assert.IsInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public void Controlador_RevisarConcepto_EsPostConRutaYAuthorize()
        {
            var metodo = typeof(PagosController).GetMethod(nameof(PagosController.RevisarConcepto));
            Assert.IsNotNull(metodo.GetCustomAttributes(typeof(System.Web.Http.HttpPostAttribute), false).SingleOrDefault());
            Assert.IsNotNull(metodo.GetCustomAttributes(typeof(System.Web.Http.AuthorizeAttribute), false).SingleOrDefault());
            var ruta = (System.Web.Http.RouteAttribute)metodo.GetCustomAttributes(typeof(System.Web.Http.RouteAttribute), false).Single();
            Assert.AreEqual("RevisarConcepto", ruta.Template);
        }

        [TestMethod]
        public void Contrato_NombresDePropiedades()
        {
            CollectionAssert.AreEquivalent(new[] { "Concepto", "Empresa", "Cliente" },
                typeof(SolicitudRevisarConcepto).GetProperties().Select(p => p.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { "Original", "Propuesto", "HayCambios", "Cambios" },
                typeof(RespuestaRevisarConcepto).GetProperties().Select(p => p.Name).ToArray());
            CollectionAssert.AreEquivalent(new[] { "De", "A" },
                typeof(CambioConcepto).GetProperties().Select(p => p.Name).ToArray());
        }

        #endregion
    }
}
