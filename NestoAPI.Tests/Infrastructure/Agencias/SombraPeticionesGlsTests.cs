using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Gls;
using NestoAPI.Models;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#552 (fase 1, sombra): se toman las peticiones GrabaServicios que mandó Nesto (AgenciasLlamadasWeb),
    /// se busca el envío por su código de barras, la API construye la suya y se comparan. Sin enviar nada.
    /// </summary>
    [TestClass]
    public class SombraPeticionesGlsTests
    {
        private static readonly Empresa EMPRESA = new Empresa
        {
            Número = "1  ", Nombre = "Nueva Visión, S.A.", Dirección = "C/ Río Tiétar, 11 - Nave 22", Población = "ALGETE",
            Provincia = "Madrid", CodPostal = "28119", Teléfono = "916281914", Email = "nuevavision@nuevavision.es"
        };

        private static EnviosAgencia Envio(int numero) => new EnviosAgencia
        {
            Numero = numero, Empresa = "1  ", Cliente = "15191     ", Pedido = 900000 + numero, Fecha = new DateTime(2026, 9, 30),
            Servicio = 96, Horario = 18, Bultos = 1, Nombre = "CLIENTE " + numero, Direccion = "C/ PRUEBA, " + numero,
            Poblacion = "MADRID", Provincia = "MADRID", Pais = 34, CodPostal = "28001", Telefono = "910000000", Movil = "",
            Email = "", Observaciones = "", Atencion = "", Reembolso = 0M, CodigoBarras = "6119714" + numero.ToString("D7")
        };

        private static PeticionGlsGuardada Guardada(int llamada, EnviosAgencia comoLaMandoNesto) => new PeticionGlsGuardada
        {
            Llamada = llamada,
            Fecha = new DateTime(2026, 9, 30, 12, 0, 0),
            Cuerpo = PeticionGls.ConstruirSoap(PeticionGls.ConstruirServicios(comoLaMandoNesto, EMPRESA, "AGENCIA"))
        };

        private static Func<string, EnvioParaPeticionGls> Buscador(params EnviosAgencia[] envios)
            => codigo => envios.Where(e => e.CodigoBarras == codigo)
                .Select(e => new EnvioParaPeticionGls { Envio = e, Empresa = EMPRESA, IdentificadorAgencia = "AGENCIA" })
                .FirstOrDefault();

        [TestMethod]
        public void Comparar_EnvioSinCambios_CuentaComoIgualYNoSaleEnElDetalle()
        {
            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(1, Envio(10)) }, Buscador(Envio(10)));

            Assert.AreEqual(1, resultado.Peticiones);
            Assert.AreEqual(1, resultado.Iguales);
            Assert.AreEqual(0, resultado.Detalle.Count);
        }

        [TestMethod]
        public void Comparar_EnvioCambiado_SaleEnElDetalleConSusCamposYSeCuentaPorCampo()
        {
            EnviosAgencia hoy = Envio(11);
            hoy.CodPostal = "28002";

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(7, Envio(11)), Guardada(8, Envio(10)) }, Buscador(hoy, Envio(10)));

            Assert.AreEqual(2, resultado.Peticiones);
            Assert.AreEqual(1, resultado.Iguales);
            Assert.AreEqual(1, resultado.ConDiferencias);
            ComparacionPeticionGls detalle = resultado.Detalle.Single();
            Assert.AreEqual(7, detalle.Llamada);
            Assert.AreEqual(11, detalle.Envio);
            Assert.AreEqual("61197140000011", detalle.CodigoBarras);
            Assert.AreEqual("Envio/Destinatario/CP", detalle.Diferencias.Single().Campo);
            Assert.AreEqual(1, resultado.CamposConDiferencias["Envio/Destinatario/CP"]);
        }

        [TestMethod]
        public void Comparar_FechaDelEnvioReescritaAlTramitar_NoEsDiferenciaPeroSeCuenta()
        {
            // Al tramitar, el envío pasa a tener la fecha de ese día (TramitacionEnviosService): la que mandó Nesto ya
            // no está en la BD. Comprobado en las 165 peticiones del 24/09 al 01/10/26.
            EnviosAgencia hoy = Envio(14);
            hoy.Fecha = new DateTime(2026, 9, 30);
            EnviosAgencia comoLaMandoNesto = Envio(14);
            comoLaMandoNesto.Fecha = new DateTime(2026, 9, 22);

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(9, comoLaMandoNesto) }, Buscador(hoy));

            Assert.AreEqual(1, resultado.Iguales);
            Assert.AreEqual(1, resultado.FechasReescritasAlTramitar);
        }

        [TestMethod]
        public void Comparar_FechaDistintaQueNoEsLaDelDiaDeLaLlamada_SiEsDiferencia()
        {
            EnviosAgencia hoy = Envio(15);
            hoy.Fecha = new DateTime(2026, 10, 5);

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(10, Envio(15)) }, Buscador(hoy));

            Assert.AreEqual("Envio/Fecha", resultado.Detalle.Single().Diferencias.Single().Campo);
            Assert.AreEqual(0, resultado.FechasReescritasAlTramitar);
        }

        [TestMethod]
        public void Comparar_ObservacionesConSaltosDeLinea_SonIguales()
        {
            // Al leer el XML guardado, \r\n se convierte en \n (normalización de XML); en la BD sigue con \r\n.
            EnviosAgencia conSaltos = Envio(16);
            conSaltos.Observaciones = "LINEA 1\r\nLINEA 2";

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(11, conSaltos) }, Buscador(conSaltos));

            Assert.AreEqual(1, resultado.Iguales);
        }

        [TestMethod]
        public void Comparar_ObservacionesCortadasEnMedioDeUnSalto_SonIguales()
        {
            // Observaciones es varchar(80): si el corte cae entre \r y \n, queda un \r suelto, que el XML lee como \n.
            EnviosAgencia cortadas = Envio(18);
            cortadas.Observaciones = "ULLWCFRXO \r\nLOURDES\r\nJAMON, 25\r";

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(13, cortadas) }, Buscador(cortadas));

            Assert.AreEqual(1, resultado.Iguales);
        }

        [TestMethod]
        public void Comparar_ObservacionesSoloConSaltosDeLinea_SonIguales()
        {
            // Caso real (envío 249149): Nesto mandó «\r\n\r\n», que un Parse normal descarta por ser solo espacios.
            EnviosAgencia soloSaltos = Envio(17);
            soloSaltos.Observaciones = "\r\n\r\n";

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(12, soloSaltos) }, Buscador(soloSaltos));

            Assert.AreEqual(1, resultado.Iguales);
        }

        [TestMethod]
        public void Comparar_CampoCambiadoAManoDespuesDeTramitar_NoEsDiferenciaPeroSeCuenta()
        {
            // Caso real: el reembolso del envío 248269 se cambió el 15/09 en la ventana de Agencias,
            // después de tramitarlo (EnviosHistoria). La petición de Nesto era correcta en su momento.
            EnviosAgencia hoy = Envio(19);
            hoy.Reembolso = 199.66M;
            EnviosAgencia comoLaMandoNesto = Envio(19);
            comoLaMandoNesto.Reembolso = 190.66M;

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(
                new[] { Guardada(14, comoLaMandoNesto) }, Buscador(hoy),
                (envio, fecha) => envio == 19 ? new HashSet<string> { "Envio/Importes/Reembolso" } : new HashSet<string>());

            Assert.AreEqual(1, resultado.Iguales);
            Assert.AreEqual(1, resultado.CambiadasDespues);
            Assert.AreEqual(0, resultado.Detalle.Count);
        }

        [TestMethod]
        public void Comparar_CampoCambiadoDespuesPeroOtraDiferencia_SigueSaliendoLaOtra()
        {
            EnviosAgencia hoy = Envio(20);
            hoy.Reembolso = 10M;
            hoy.Bultos = 4;

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(
                new[] { Guardada(15, Envio(20)) }, Buscador(hoy),
                (envio, fecha) => new HashSet<string> { "Envio/Importes/Reembolso" });

            Assert.AreEqual("Envio/Bultos", resultado.Detalle.Single().Diferencias.Single().Campo);
        }

        [DataTestMethod]
        [DataRow("Reembolso", "Envio/Importes/Reembolso")]
        [DataRow("Retorno", "Envio/Retorno")]
        [DataRow("Estado", null)]
        public void CampoDeLaPeticion_TraduceElCampoDeEnviosHistoria(string campoHistoria, string campoPeticion)
        {
            Assert.AreEqual(campoPeticion, SombraPeticionesGls.CampoDeLaPeticion(campoHistoria));
        }

        [TestMethod]
        public void Resumen_SinDiferencias_EsNull()
        {
            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(1, Envio(10)) }, Buscador(Envio(10)));

            Assert.IsNull(SombraPeticionesGls.ResumenParaAviso(resultado, new DateTime(2026, 9, 30)));
        }

        [TestMethod]
        public void Resumen_ConDiferencias_DiceCuantasYQueCamposYLasPrimerasLlamadas()
        {
            EnviosAgencia hoy = Envio(11);
            hoy.CodPostal = "28002";
            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(7, Envio(11)) }, Buscador(hoy));

            string resumen = SombraPeticionesGls.ResumenParaAviso(resultado, new DateTime(2026, 9, 30));

            StringAssert.Contains(resumen, "#552");
            StringAssert.Contains(resumen, "30/09/2026");
            StringAssert.Contains(resumen, "1 de 1");
            StringAssert.Contains(resumen, "Envio/Destinatario/CP");
            StringAssert.Contains(resumen, "llamada 7");
            StringAssert.Contains(resumen, "envío 11");
        }

        [TestMethod]
        public void Comparar_PeticionSinEnvioEnLaBaseDeDatos_SeCuentaAparte()
        {
            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { Guardada(3, Envio(12)) }, Buscador());

            Assert.AreEqual(1, resultado.SinEnvio);
            Assert.AreEqual(0, resultado.Iguales);
            Assert.IsNull(resultado.Detalle.Single().Envio);
        }

        [TestMethod]
        public void Comparar_CuerposQueNoSonPeticiones_SeIgnoran()
        {
            var seguimiento = new PeticionGlsGuardada { Llamada = 4, Cuerpo = "Consultar seguimiento envío 249315 (pedido 927329)" };

            ResultadoSombraGls resultado = SombraPeticionesGls.Comparar(new[] { seguimiento }, Buscador());

            Assert.AreEqual(0, resultado.Peticiones);
            Assert.AreEqual(1, resultado.Ignoradas);
        }

        [TestMethod]
        public void CodigosDeBarras_DeLasPeticionesGuardadas_SinRepetir()
        {
            List<string> codigos = SombraPeticionesGls.CodigosDeBarras(new[] { Guardada(1, Envio(10)), Guardada(2, Envio(10)), Guardada(3, Envio(13)) });

            CollectionAssert.AreEquivalent(new[] { "61197140000010", "61197140000013" }, codigos);
        }
    }
}
