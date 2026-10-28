using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Models;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#725 (Carlos, 28/10/26, envío 249990): los datos de un envío que viajan a la agencia al registrarlo y la
    /// comparación entre lo registrado y lo que manda el PUT. Pura.
    /// </summary>
    [TestClass]
    public class DatosQueViajaronALaAgenciaTests
    {
        private static EnviosAgencia Envio() => new EnviosAgencia
        {
            Numero = 249990,
            Agencia = Constantes.Agencias.AGENCIA_CTT,
            Estado = Constantes.Agencias.ESTADO_EN_CURSO,
            CodigoBarras = "0082800082809772552452",
            Reembolso = 0M,
            Retorno = 0,
            Servicio = 48,
            Horario = 0,
            Nombre = "AIDA ESTETICA          ",
            Direccion = "Calle Mayor 1",
            CodPostal = "28001",
            Poblacion = "MADRID",
            Pais = 34,
            Telefono = "910000000",
            Movil = "600000000",
            Email = "aida@example.com",
            Bultos = 1,
            Peso = 2.5M,
            Observaciones = "Entregar por la mañana",
            Atencion = "Aida"
        };

        [TestMethod]
        public void Comparar_SinCambios_NoHayNada()
        {
            Assert.AreEqual(0, DatosQueViajaronALaAgencia.Comparar(Envio(), Envio()).Count);
        }

        [TestMethod]
        public void Comparar_RetornoDelEnvio249990_Retorno()
        {
            EnviosAgencia enviado = Envio();
            enviado.Retorno = 1;

            CollectionAssert.AreEqual(new List<string> { "Retorno" }, DatosQueViajaronALaAgencia.Comparar(Envio(), enviado));
        }

        [TestMethod]
        public void Comparar_VariosDatosQueViajaron_TodosEnOrden()
        {
            EnviosAgencia enviado = Envio();
            enviado.Reembolso = 121.50M;
            enviado.Direccion = "Calle Menor 2";
            enviado.CodPostal = "28002";
            enviado.Telefono = "911111111";
            enviado.Bultos = 2;
            enviado.Peso = 5M;
            enviado.Observaciones = "Llamar antes";
            enviado.Servicio = 24;

            CollectionAssert.AreEqual(
                new List<string> { "Reembolso", "Servicio", "Dirección", "Código postal", "Teléfono", "Bultos", "Peso", "Observaciones" },
                DatosQueViajaronALaAgencia.Comparar(Envio(), enviado));
        }

        [TestMethod]
        public void Comparar_DatosQueNoViajan_NoCuentan()
        {
            // Horario (no viaja ni a CTT ni a Innovatrans), atención, estado y fechas son nuestros.
            EnviosAgencia enviado = Envio();
            enviado.Horario = 18;
            enviado.Atencion = "Otra persona";
            enviado.Estado = Constantes.Agencias.ESTADO_TRAMITADO;
            enviado.FechaEntrega = new System.DateTime(2026, 10, 29);

            Assert.AreEqual(0, DatosQueViajaronALaAgencia.Comparar(Envio(), enviado).Count);
        }

        [TestMethod]
        public void Comparar_EspaciosDeRellenoYNulos_NoSonCambios()
        {
            EnviosAgencia registrado = Envio();
            registrado.Movil = null;
            EnviosAgencia enviado = Envio();
            enviado.Nombre = "AIDA ESTETICA";
            enviado.Movil = "   ";

            Assert.AreEqual(0, DatosQueViajaronALaAgencia.Comparar(registrado, enviado).Count);
        }

        [TestMethod]
        public void Comparar_ReembolsoCentinelaNegativo_EsComoCero()
        {
            EnviosAgencia registrado = Envio();
            registrado.Reembolso = -1M;

            Assert.AreEqual(0, DatosQueViajaronALaAgencia.Comparar(registrado, Envio()).Count);
        }

        [TestMethod]
        public void Diferencias_TraeLosValoresAnteriorYNuevo()
        {
            EnviosAgencia registrado = Envio();
            registrado.Reembolso = 500M;
            EnviosAgencia enviado = Envio();

            DatosQueViajaronALaAgencia.CampoCambiado cambio = DatosQueViajaronALaAgencia.Diferencias(registrado, enviado).Single();

            Assert.AreEqual("Reembolso", cambio.Campo);
            StringAssert.StartsWith(cambio.Anterior, "500,00");
            StringAssert.StartsWith(cambio.Nuevo, "0,00");
        }

        [TestMethod]
        public void Campos_IncluyeReembolsoRetornoYLaDireccion()
        {
            IReadOnlyList<string> campos = DatosQueViajaronALaAgencia.Campos;

            CollectionAssert.IsSubsetOf(new[] { "Reembolso", "Retorno", "Servicio", "Nombre", "Dirección", "Código postal", "Población",
                "Teléfono", "Bultos", "Peso", "Observaciones" }, campos.ToList());
            CollectionAssert.DoesNotContain(campos.ToList(), "Horario");
        }

        [TestMethod]
        public void EstaRegistradoEnLaAgencia_SoloConAlbaranEnCursoYGestionRemota()
        {
            Assert.IsTrue(DatosQueViajaronALaAgencia.EstaRegistradoEnLaAgencia("0082800082809772552452", Constantes.Agencias.ESTADO_EN_CURSO, true));
            Assert.IsTrue(DatosQueViajaronALaAgencia.EstaRegistradoEnLaAgencia("0082800082809772552452", Constantes.Agencias.ESTADO_TRAMITADO, true));
            Assert.IsFalse(DatosQueViajaronALaAgencia.EstaRegistradoEnLaAgencia(null, Constantes.Agencias.ESTADO_EN_CURSO, true), "sin albarán: aún no ha viajado");
            Assert.IsFalse(DatosQueViajaronALaAgencia.EstaRegistradoEnLaAgencia("  ", Constantes.Agencias.ESTADO_EN_CURSO, true));
            Assert.IsFalse(DatosQueViajaronALaAgencia.EstaRegistradoEnLaAgencia("0082800082809772552452", Constantes.Agencias.ESTADO_PENDIENTE, true), "pendiente");
            Assert.IsFalse(DatosQueViajaronALaAgencia.EstaRegistradoEnLaAgencia("61197140248079", Constantes.Agencias.ESTADO_EN_CURSO, false), "GLS lo manda al cierre");
        }

        [TestMethod]
        public void MensajeCambioSinAgencia_NombraLosCamposYElOtroBoton()
        {
            string mensaje = DatosQueViajaronALaAgencia.MensajeCambioSinAgencia(249990, new[] { "Retorno", "Reembolso" });

            StringAssert.Contains(mensaje, "249990");
            StringAssert.Contains(mensaje, "Retorno, Reembolso");
            StringAssert.Contains(mensaje, "NO en el de la agencia");
            StringAssert.Contains(mensaje, "Modificar datos y reenviar a la agencia");
        }
    }
}
