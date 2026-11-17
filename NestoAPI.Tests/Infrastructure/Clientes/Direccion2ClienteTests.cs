using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Direcciones;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Direcciones;
using NestoAPI.Models.PedidosVenta;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#874: «¿Qué es?» (Dirección 2), el número propio cuando Google da la calle sin él y la Dirección 2 en el envío.
    /// </summary>
    [TestClass]
    public class Direccion2ClienteTests
    {
        // ---------------------------------------------------------------- regla pura de la Dirección 2

        [TestMethod]
        public void Componer_Piso_PisoYPuertaEnMayusculas()
        {
            ResultadoDireccion2 r = Direccion2Cliente.Componer("Piso", "1", "d", null, null, null, null);
            Assert.IsTrue(r.Valida);
            Assert.AreEqual("1º D", r.Direccion2);
            Assert.AreEqual(TiposLugar.PISO, r.TipoLugar);

            Assert.AreEqual("1º D", Direccion2Cliente.Componer("piso", "1º", "D", null, null, null, null).Direccion2, "ya con º no se duplica");
            Assert.AreEqual("BAJO B", Direccion2Cliente.Componer("PISO", "bajo", "b", null, null, null, null).Direccion2);
        }

        [TestMethod]
        public void Componer_PisoSinPuertaOSinPiso_NoVale()
        {
            Assert.AreEqual(Direccion2Cliente.MOTIVO_PISO, Direccion2Cliente.Componer("Piso", "1", " ", null, null, null, null).Motivo);
            Assert.AreEqual(Direccion2Cliente.MOTIVO_PISO, Direccion2Cliente.Componer("Piso", null, "D", null, null, null, null).Motivo);
        }

        [TestMethod]
        public void Componer_Local_ConYSinNumero_RotuloConTildes()
        {
            ResultadoDireccion2 r = Direccion2Cliente.Componer("Local", null, null, "6", "El Edén", null, null);
            Assert.IsTrue(r.Valida);
            Assert.AreEqual("LOCAL 6 · RÓTULO: EL EDÉN", r.Direccion2);
            Assert.AreEqual("LOCAL · RÓTULO: EL EDÉN", Direccion2Cliente.Componer("Local", null, null, "  ", "el edén", null, null).Direccion2,
                "el número de local es opcional");
        }

        [TestMethod]
        public void Componer_LocalSinRotulo_NoVale()
        {
            ResultadoDireccion2 r = Direccion2Cliente.Componer("Local", null, null, "6", "   ", null, null);
            Assert.IsFalse(r.Valida);
            Assert.AreEqual(Direccion2Cliente.MOTIVO_LOCAL, r.Motivo);
        }

        [TestMethod]
        public void Componer_CasaNaveYOtro()
        {
            Assert.AreEqual("CASA", Direccion2Cliente.Componer("Casa", "1", "D", "6", "X", "4", "Y").Direccion2, "en una casa no cuenta nada más");
            Assert.AreEqual("NAVE 4", Direccion2Cliente.Componer("Nave", null, null, null, null, "4", null).Direccion2);
            Assert.AreEqual(Direccion2Cliente.MOTIVO_NAVE, Direccion2Cliente.Componer("Nave", null, null, null, null, "", null).Motivo);
            Assert.AreEqual("CENTRO COMERCIAL PLAZA, PLANTA 2",
                Direccion2Cliente.Componer("Otro", null, null, null, null, null, " centro comercial   plaza, planta 2 ").Direccion2);
            Assert.AreEqual(Direccion2Cliente.MOTIVO_OTRO, Direccion2Cliente.Componer("Otro", null, null, null, null, null, null).Motivo);
        }

        [TestMethod]
        public void Componer_SinTipoOTipoDesconocido_NoVale()
        {
            Assert.AreEqual(Direccion2Cliente.MOTIVO_SIN_TIPO, Direccion2Cliente.Componer(null, "1", "D", null, null, null, null).Motivo);
            ResultadoDireccion2 r = Direccion2Cliente.Componer("Ático", null, null, null, null, null, null);
            Assert.IsFalse(r.Valida);
            StringAssert.Contains(r.Motivo, "Piso, Local, Casa, Nave u Otro");
        }

        [TestMethod]
        public void Componer_DeMasDe100_NoVale()
        {
            ResultadoDireccion2 r = Direccion2Cliente.Componer("Otro", null, null, null, null, null, new string('a', 101));
            Assert.IsFalse(r.Valida);
            StringAssert.Contains(r.Motivo, "100");
            Assert.IsTrue(Direccion2Cliente.Componer("Otro", null, null, null, null, null, new string('a', 100)).Valida);
        }

        [TestMethod]
        public void ParaDireccion_SinDireccion_NadaQueDecir()
        {
            Assert.IsTrue(Direccion2Cliente.ParaDireccion(new ClienteCrear { Direccion = "  " }, exigir: true).Valida);
            Assert.IsNull(Direccion2Cliente.ParaDireccion(new ClienteCrear { Direccion = null }, exigir: true).Direccion2);
            Assert.IsTrue(Direccion2Cliente.ParaDireccion(null, exigir: true).Valida);
        }

        [TestMethod]
        public void ParaDireccion_SinTipo_SoloRechazaConElInterruptor()
        {
            var alta = new ClienteCrear { Direccion = "C/ SOR LIVIA ALCORTA, 13" };
            Assert.IsTrue(Direccion2Cliente.ParaDireccion(alta, exigir: false).Valida, "NestoApp sin su gemela sigue dando altas");
            Assert.IsNull(Direccion2Cliente.ParaDireccion(alta, exigir: false).Direccion2);
            Assert.AreEqual(Direccion2Cliente.MOTIVO_SIN_TIPO, Direccion2Cliente.ParaDireccion(alta, exigir: true).Motivo);
        }

        [TestMethod]
        public void ParaDireccion_ConTipo_SeValidaAunqueElInterruptorEsteApagado()
        {
            var alta = new ClienteCrear { Direccion = "C/ SOR LIVIA ALCORTA, 13", TipoLugar = "Local", NumeroLocal = "6" };
            Assert.AreEqual(Direccion2Cliente.MOTIVO_LOCAL, Direccion2Cliente.ParaDireccion(alta, exigir: false).Motivo);
            alta.Rotulo = "El Edén";
            Assert.AreEqual("LOCAL 6 · RÓTULO: EL EDÉN", Direccion2Cliente.ParaDireccion(alta, exigir: false).Direccion2);
        }

        // ---------------------------------------------------------------- número propio (puro)

        [TestMethod]
        public void NumeroPropio_Normalizar()
        {
            Assert.AreEqual("13", NumeroPropioDireccion.Normalizar(" 13 "));
            Assert.AreEqual("13B", NumeroPropioDireccion.Normalizar("13 b"));
            Assert.AreEqual("13BIS", NumeroPropioDireccion.Normalizar("13 bis"));
            Assert.AreEqual("S/N", NumeroPropioDireccion.Normalizar("s/n"));
            Assert.AreEqual("S/N", NumeroPropioDireccion.Normalizar("SN"));
            Assert.AreEqual("S/N", NumeroPropioDireccion.Normalizar("sin número"));
            Assert.IsNull(NumeroPropioDireccion.Normalizar("  "));
        }

        [TestMethod]
        public void NumeroPropio_FormatoValido()
        {
            foreach (string valido in new[] { "99", " 13 ", "13 B", "13b", "13 BIS", "13 bis", "S/N", "s/n", "SN", "sin número" })
            {
                Assert.IsTrue(NumeroPropioDireccion.FormatoValido(valido), valido);
            }
            // Carlos 17/11/26: en «Número» solo va el portal; lo demás, en «¿Qué es?».
            foreach (string noValido in new[] { "nº 99", "Nº13", "n 99", "99 local 6", "13, local 6", "Edificio Rojo", "trece", "13 BB", "123456", "", null })
            {
                Assert.IsFalse(NumeroPropioDireccion.FormatoValido(noValido), noValido ?? "null");
            }
        }

        [TestMethod]
        public void NumeroPropio_Confirmado_MismoPortalYMismoCodigoPostal()
        {
            Assert.IsTrue(NumeroPropioDireccion.Confirmado(new DireccionDetalleDTO { Numero = "13", CodigoPostal = "28100" }, "13", "28100"));
            Assert.IsTrue(NumeroPropioDireccion.Confirmado(new DireccionDetalleDTO { Numero = "13", CodigoPostal = "28100" }, "13B", "28100"), "la letra rara vez la sabe Google");
            Assert.IsTrue(NumeroPropioDireccion.Confirmado(new DireccionDetalleDTO { Numero = "13" }, "13", "28100"), "sin CP de Google basta el número");
            Assert.IsFalse(NumeroPropioDireccion.Confirmado(new DireccionDetalleDTO { Numero = null, CodigoPostal = "28100" }, "13", "28100"), "solo la calle: no confirma");
            Assert.IsFalse(NumeroPropioDireccion.Confirmado(new DireccionDetalleDTO { Numero = "15", CodigoPostal = "28100" }, "13", "28100"));
            Assert.IsFalse(NumeroPropioDireccion.Confirmado(new DireccionDetalleDTO { Numero = "13", CodigoPostal = "28200" }, "13", "28100"));
            Assert.IsFalse(NumeroPropioDireccion.Confirmado(null, "13", "28100"));
        }

        [TestMethod]
        public void NumeroPropio_DireccionLlevaElNumero()
        {
            Assert.IsTrue(NumeroPropioDireccion.DireccionLlevaElNumero("C/ SOR LIVIA ALCORTA, 13", "13"));
            Assert.IsFalse(NumeroPropioDireccion.DireccionLlevaElNumero("C/ SOR LIVIA ALCORTA, 113", "13"));
            Assert.IsTrue(NumeroPropioDireccion.DireccionLlevaElNumero("C/ SOR LIVIA ALCORTA, S/N", "s/n"));
        }

        // ---------------------------------------------------------------- número propio en el gestor

        private static GestorClientes Gestor(IServicioDireccionesGoogle google, string exigirDireccion2 = null)
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.CogerDatosCodigoPostal(A<string>._)).ReturnsLazily((string cp) => new RespuestaDatosGeneralesClientes { CodigoPostal = cp, Poblacion = "ALCOBENDAS" });
            A.CallTo(() => servicio.ClientesMismoTelefono(A<string>._)).Returns(Task.FromResult(new List<ClienteTelefonoLookup>()));
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Returns(null);
            A.CallTo(() => lector.LeerParametro(A<string>._, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, Constantes.ParametrosUsuario.EXIGIR_DIRECCION2_ALTA))
                .Returns(exigirDireccion2);
            return new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(A.Fake<ISincronizacionEventPublisher>()))
            {
                DireccionesGoogle = google,
                LectorParametros = lector
            };
        }

        private static IServicioDireccionesGoogle Google(DireccionDetalleDTO respuesta)
        {
            IServicioDireccionesGoogle google = A.Fake<IServicioDireccionesGoogle>();
            A.CallTo(() => google.Geocodificar(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(respuesta));
            return google;
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_ConNumeroQueGoogleEncuentra_LoDevuelveDentroDeLaDireccion()
        {
            IServicioDireccionesGoogle google = Google(new DireccionDetalleDTO { Calle = "Calle Sor Livia Alcorta", Numero = "13", CodigoPostal = "28100" });

            RespuestaDatosGeneralesClientes r = await Gestor(google).ComprobarDatosGenerales("Calle Sor Livia Alcorta", "28100", "916000000",
                direccionVerificada: true, pais: "ES", numero: "13");

            Assert.AreEqual("C/ SOR LIVIA ALCORTA, 13", r.DireccionFormateada);
            A.CallTo(() => google.Geocodificar("Calle Sor Livia Alcorta, 13", "28100", "ES")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_ConNumeroValidoQueGoogleNoEncuentra_NoBloqueaYDevuelveLaPregunta()
        {
            IServicioDireccionesGoogle google = Google(new DireccionDetalleDTO { Calle = "Calle La Granja", CodigoPostal = "28108" });

            RespuestaDatosGeneralesClientes r = await Gestor(google).ComprobarDatosGenerales("Calle La Granja", "28108", "916000000",
                direccionVerificada: true, pais: "ES", numero: "99");

            Assert.AreEqual("C/ LA GRANJA, 99", r.DireccionFormateada);
            Assert.IsTrue(r.NumeroSinConfirmarPorGoogle);
            Assert.AreEqual("Google Maps no encuentra el número 99 en C/ LA GRANJA (28108). Es posible que el repartidor de la agencia " +
                "no lo encuentre. ¿Quiere guardarlo igualmente?", r.AvisoNumero);
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_ConNumeroQueGoogleEncuentra_SinPregunta()
        {
            RespuestaDatosGeneralesClientes r = await Gestor(Google(new DireccionDetalleDTO { Numero = "99", CodigoPostal = "28108" }))
                .ComprobarDatosGenerales("Calle La Granja", "28108", "916000000", direccionVerificada: true, pais: "ES", numero: "99");

            Assert.IsFalse(r.NumeroSinConfirmarPorGoogle);
            Assert.IsNull(r.AvisoNumero);
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_SinNumero_NoPreguntaAGoogle()
        {
            IServicioDireccionesGoogle google = Google(null);

            RespuestaDatosGeneralesClientes r = await Gestor(google).ComprobarDatosGenerales("Calle Sor Livia Alcorta", "28100", "916000000",
                direccionVerificada: true, pais: "ES", numero: "s/n");

            Assert.AreEqual("C/ SOR LIVIA ALCORTA, S/N", r.DireccionFormateada);
            A.CallTo(() => google.Geocodificar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ComprobarDatosGenerales_NumeroNoValido_400Siempre()
        {
            foreach (string noValido in new[] { "13 local 6", "nº 99", "Edificio Rojo" })
            {
                ArgumentException ex = await Assert.ThrowsExceptionAsync<ArgumentException>(() => Gestor(Google(null)).ComprobarDatosGenerales(
                    "Calle Sor Livia Alcorta", "28100", "916000000", direccionVerificada: true, pais: "ES", numero: noValido));
                Assert.AreEqual(NumeroPropioDireccion.MOTIVO_FORMATO, ex.Message);
            }
            StringAssert.Contains(NumeroPropioDireccion.MOTIVO_FORMATO, "solo va el número del portal");
        }

        [TestMethod]
        public async Task ComprobarNumeroPropio_AltaVuelveAComprobar()
        {
            var alta = new ClienteCrear { Direccion = "C/ SOR LIVIA ALCORTA, 13", CodigoPostal = "28100", DireccionVerificada = true, Numero = "13" };

            ResultadoNumeroPropio encontrado = await Gestor(Google(new DireccionDetalleDTO { Numero = "13", CodigoPostal = "28100" })).ComprobarNumeroPropio(alta);
            Assert.IsNull(encontrado.Motivo);
            Assert.IsFalse(encontrado.ConfirmadoSinGoogle);

            ResultadoNumeroPropio sinConfirmar = await Gestor(Google(new DireccionDetalleDTO { CodigoPostal = "28100" })).ComprobarNumeroPropio(alta);
            StringAssert.Contains(sinConfirmar.Motivo, "¿Quiere guardarlo igualmente?");

            alta.Direccion = "C/ SOR LIVIA ALCORTA, 15";
            StringAssert.Contains((await Gestor(Google(null)).ComprobarNumeroPropio(alta)).Motivo, "no lleva el número 13");
        }

        [TestMethod]
        public async Task ComprobarNumeroPropio_ValidoNoEncontradoYConfirmado_SigueYDejaRastro()
        {
            var alta = new ClienteCrear
            {
                Direccion = "C/ LA GRANJA, 99", CodigoPostal = "28108", DireccionVerificada = true, Numero = "99", NumeroConfirmadoSinGoogle = true
            };

            ResultadoNumeroPropio r = await Gestor(Google(new DireccionDetalleDTO { CodigoPostal = "28108" })).ComprobarNumeroPropio(alta);

            Assert.IsNull(r.Motivo);
            Assert.IsTrue(r.ConfirmadoSinGoogle, "Se apuntará en Modificaciones tras guardar");
        }

        [TestMethod]
        public async Task ComprobarNumeroPropio_NoValido_400AunqueVengaConfirmado()
        {
            var alta = new ClienteCrear
            {
                Direccion = "C/ LA GRANJA, 99", CodigoPostal = "28108", DireccionVerificada = true, Numero = "nº 99", NumeroConfirmadoSinGoogle = true
            };

            Assert.AreEqual(NumeroPropioDireccion.MOTIVO_FORMATO, (await Gestor(Google(null)).ComprobarNumeroPropio(alta)).Motivo);
        }

        [TestMethod]
        public async Task ComprobarNumeroPropio_GoogleCaido_ElAltaSigue()
        {
            IServicioDireccionesGoogle google = A.Fake<IServicioDireccionesGoogle>();
            A.CallTo(() => google.Geocodificar(A<string>._, A<string>._, A<string>._)).Throws(new System.Net.Http.HttpRequestException("sin red"));
            var alta = new ClienteCrear { Direccion = "C/ SOR LIVIA ALCORTA, 13", CodigoPostal = "28100", DireccionVerificada = true, Numero = "13" };

            ResultadoNumeroPropio r = await Gestor(google).ComprobarNumeroPropio(alta);
            Assert.IsNull(r.Motivo);
            Assert.IsFalse(r.ConfirmadoSinGoogle);
        }

        [TestMethod]
        public async Task ComprobarNumeroPropio_SinNumeroPropio_NiSePregunta()
        {
            IServicioDireccionesGoogle google = Google(null);
            Assert.IsNull((await Gestor(google).ComprobarNumeroPropio(new ClienteCrear { Direccion = "C/ MAYOR, 1", DireccionVerificada = true })).Motivo);
            A.CallTo(() => google.Geocodificar(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- alta

        [TestMethod]
        public async Task CrearCliente_InterruptorEncendidoSinQueEs_400ConElMotivo()
        {
            var alta = new ClienteCrear { Empresa = "1", Cliente = "42143", EsContacto = true, Direccion = "C/ SOR LIVIA ALCORTA, 13", CodigoPostal = "28100" };

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() => Gestor(Google(null), exigirDireccion2: "1").CrearCliente(alta, A.Fake<NVEntities>()));

            Assert.AreEqual(Direccion2Cliente.MOTIVO_SIN_TIPO, ex.Message);
        }

        [TestMethod]
        public async Task CrearCliente_LocalSinRotulo_400AunqueElInterruptorEsteApagado()
        {
            var alta = new ClienteCrear { Empresa = "1", Cliente = "42143", EsContacto = true, Direccion = "C/ SOR LIVIA ALCORTA, 13", CodigoPostal = "28100", TipoLugar = "Local", NumeroLocal = "6" };

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() => Gestor(Google(null), exigirDireccion2: "0").CrearCliente(alta, A.Fake<NVEntities>()));

            Assert.AreEqual(Direccion2Cliente.MOTIVO_LOCAL, ex.Message);
        }

        [TestMethod]
        public async Task CrearCliente_NumeroQueGoogleNoEncuentraSinConfirmar_400ConLaPregunta()
        {
            var alta = new ClienteCrear
            {
                Empresa = "1", Cliente = "42143", EsContacto = true, Direccion = "C/ SOR LIVIA ALCORTA, 13", CodigoPostal = "28100",
                DireccionVerificada = true, Numero = "13", TipoLugar = "Casa"
            };

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() =>
                Gestor(Google(new DireccionDetalleDTO { CodigoPostal = "28100" })).CrearCliente(alta, A.Fake<NVEntities>()));

            StringAssert.Contains(ex.Message, "Google Maps no encuentra el número 13");
            StringAssert.Contains(ex.Message, "¿Quiere guardarlo igualmente?");
        }

        [TestMethod]
        public async Task CrearCliente_NumeroNoValido_400AunqueVengaConfirmado()
        {
            var alta = new ClienteCrear
            {
                Empresa = "1", Cliente = "42143", EsContacto = true, Direccion = "C/ SOR LIVIA ALCORTA, 13", CodigoPostal = "28100",
                DireccionVerificada = true, Numero = "13 local 6", NumeroConfirmadoSinGoogle = true, TipoLugar = "Casa"
            };

            ValidationException ex = await Assert.ThrowsExceptionAsync<ValidationException>(() =>
                Gestor(Google(null)).CrearCliente(alta, A.Fake<NVEntities>()));

            Assert.AreEqual(NumeroPropioDireccion.MOTIVO_FORMATO, ex.Message);
        }

        [TestMethod]
        public void ExigirDireccion2Alta_SinFilaApagado()
        {
            Assert.IsFalse(Gestor(Google(null), exigirDireccion2: null).ExigirDireccion2Alta("1"));
            Assert.IsFalse(Gestor(Google(null), exigirDireccion2: "0").ExigirDireccion2Alta("1"));
            Assert.IsTrue(Gestor(Google(null), exigirDireccion2: "1 ").ExigirDireccion2Alta("1"));
        }

        [TestMethod]
        public void Guardar_SinLasColumnas_NoEscribeNada()
        {
            ColumnasOpcionales.ForzarParaTests = false;
            try
            {
                var cliente = new Cliente { Empresa = "1", Nº_Cliente = "42143", Contacto = "0" };
                Assert.IsFalse(Direccion2ClienteSql.Guardar(A.Fake<NVEntities>(), cliente, Direccion2Cliente.Componer("Casa", null, null, null, null, null, null)));
                Assert.IsNull(Direccion2ClienteSql.Leer(A.Fake<NVEntities>(), "1", "42143", "0"));
            }
            finally
            {
                ColumnasOpcionales.ForzarParaTests = null;
            }
        }

        // ---------------------------------------------------------------- envío

        [TestMethod]
        public void Envio_CabeEnLaDireccion()
        {
            DireccionEnvioCompuesta r = DireccionEnvioCliente.Componer("C/ SOR LIVIA ALCORTA, 13   ", "LOCAL 6 · RÓTULO: EL EDÉN", "Por la mañana");
            Assert.AreEqual("C/ SOR LIVIA ALCORTA, 13, LOCAL 6 · RÓTULO: EL EDÉN", r.Direccion);
            Assert.AreEqual("Por la mañana", r.Observaciones);
            Assert.IsNull(r.Aviso);
            Assert.IsFalse(r.EnObservaciones);
        }

        [TestMethod]
        public void Envio_NoCabeEnLaDireccion_VaALasObservacionesSinQuitarNada()
        {
            string calle = "AV. DE LA INDUSTRIA DEL POLIGONO INDUSTRIAL NORTE, 128";
            string d2 = "LOCAL 6 · RÓTULO: CENTRO DE ESTÉTICA EL EDÉN";
            DireccionEnvioCompuesta r = DireccionEnvioCliente.Componer(calle, d2, "Por la mañana");
            Assert.AreEqual(calle, r.Direccion);
            Assert.AreEqual(d2 + " · Por la mañana", r.Observaciones);
            Assert.IsTrue(r.EnObservaciones);
            Assert.IsNull(r.Aviso);

            Assert.AreEqual(d2, DireccionEnvioCliente.Componer(calle, d2, "  ").Observaciones);
        }

        [TestMethod]
        public void Envio_NoCabeEnNingunSitio_AvisaYNoToca()
        {
            string calle = "AV. DE LA INDUSTRIA DEL POLIGONO INDUSTRIAL NORTE, 128";
            string d2 = "LOCAL 6 · RÓTULO: CENTRO DE ESTÉTICA EL EDÉN";
            string obs = new string('x', 60);
            DireccionEnvioCompuesta r = DireccionEnvioCliente.Componer(calle, d2, obs);
            Assert.AreEqual(calle, r.Direccion);
            Assert.AreEqual(obs, r.Observaciones);
            StringAssert.Contains(r.Aviso, d2);
            StringAssert.Contains(r.Aviso, "añádala a mano");
        }

        [TestMethod]
        public void Envio_SinDireccion2_NiSeRecorta()
        {
            DireccionEnvioCompuesta r = DireccionEnvioCliente.Componer("C/ MAYOR, 1   ", null, "  obs ");
            Assert.AreEqual("C/ MAYOR, 1   ", r.Direccion);
            Assert.AreEqual("  obs ", r.Observaciones);
        }

        [TestMethod]
        public void ParaAgencia_AplicaLaDireccion2EnLaFichaYLosComentarios()
        {
            var dto = new PedidoParaAgenciaDTO { Comentarios = "Por la mañana", ClienteFicha = new ClienteParaAgenciaDTO { Direccion = "C/ SOR LIVIA ALCORTA, 13      " } };
            GestorPedidoParaAgencia.AplicarDireccion2(dto, "LOCAL 6 · RÓTULO: EL EDÉN");
            Assert.AreEqual("C/ SOR LIVIA ALCORTA, 13, LOCAL 6 · RÓTULO: EL EDÉN", dto.ClienteFicha.Direccion);
            Assert.AreEqual("Por la mañana", dto.Comentarios);
            Assert.AreEqual("LOCAL 6 · RÓTULO: EL EDÉN", dto.ClienteFicha.Direccion2);

            var sinD2 = new PedidoParaAgenciaDTO { Comentarios = "x  ", ClienteFicha = new ClienteParaAgenciaDTO { Direccion = "C/ MAYOR, 1   " } };
            GestorPedidoParaAgencia.AplicarDireccion2(sinD2, null);
            Assert.AreEqual("C/ MAYOR, 1   ", sinD2.ClienteFicha.Direccion, "sin Dirección 2, la paridad sin Trim del DTO no se toca");
            Assert.AreEqual("x  ", sinD2.Comentarios);
        }
    }
}
