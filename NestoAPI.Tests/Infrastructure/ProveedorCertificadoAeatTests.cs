using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#388: elección del certificado para los servicios de la AEAT (VNifV2) entre los
    /// candidatos disponibles (almacén de Windows LocalMachine\My + .pfx de fallback).
    /// Reglas: solo certificados de la empresa, vigentes y con clave privada; entre los válidos
    /// gana el de caducidad más lejana (así el renovado se impone solo al importarlo, sin
    /// reciclar la aplicación ni redesplegar).
    /// </summary>
    [TestClass]
    public class ProveedorCertificadoAeatTests
    {
        private static readonly DateTime Ahora = new DateTime(2026, 8, 18, 12, 0, 0);

        // Subject real de un certificado FNMT de representante (NIF de persona anonimizado):
        // el identificador de la empresa aparece dos veces, como VATES- y como (R: ...).
        private const string SUBJECT_REPRESENTANTE =
            "C=ES, O=NUEVA VISION SA, OID.2.5.4.97=VATES-A78368255, " +
            "CN=00000000T CARLOS ADRIAN (R: A78368255), SERIALNUMBER=IDCES-00000000T";

        private const string NIF_EMPRESA = "A78368255";

        private static CandidatoCertificado Candidato(
            DateTime notBefore, DateTime notAfter,
            string subject = SUBJECT_REPRESENTANTE, bool clavePrivada = true)
        {
            return new CandidatoCertificado
            {
                Subject = subject,
                NotBefore = notBefore,
                NotAfter = notAfter,
                TieneClavePrivada = clavePrivada
            };
        }

        [TestMethod]
        public void Elegir_UnicoCandidatoVigente_LoDevuelve()
        {
            CandidatoCertificado vigente = Candidato(Ahora.AddYears(-1), Ahora.AddDays(3));

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { vigente }, Ahora, NIF_EMPRESA);

            Assert.AreSame(vigente, elegido);
        }

        [TestMethod]
        public void Elegir_VariosVigentes_GanaElDeCaducidadMasLejana()
        {
            // El caso de la renovación: conviven el que caduca en días y el recién importado.
            CandidatoCertificado caducaEnDias = Candidato(Ahora.AddYears(-2), Ahora.AddDays(3));
            CandidatoCertificado renovado = Candidato(Ahora.AddDays(-1), Ahora.AddYears(2));

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { caducaEnDias, renovado }, Ahora, NIF_EMPRESA);

            Assert.AreSame(renovado, elegido);
        }

        [TestMethod]
        public void Elegir_Caducado_NoSeElige()
        {
            CandidatoCertificado caducado = Candidato(Ahora.AddYears(-3), Ahora.AddDays(-1));

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { caducado }, Ahora, NIF_EMPRESA);

            Assert.IsNull(elegido);
        }

        [TestMethod]
        public void Elegir_CaducadoYVigente_DevuelveElVigente()
        {
            CandidatoCertificado caducado = Candidato(Ahora.AddYears(-3), Ahora.AddDays(-1));
            CandidatoCertificado vigente = Candidato(Ahora.AddYears(-1), Ahora.AddYears(1));

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { caducado, vigente }, Ahora, NIF_EMPRESA);

            Assert.AreSame(vigente, elegido);
        }

        [TestMethod]
        public void Elegir_AunNoValido_NoSeElige()
        {
            CandidatoCertificado futuro = Candidato(Ahora.AddDays(1), Ahora.AddYears(2));

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { futuro }, Ahora, NIF_EMPRESA);

            Assert.IsNull(elegido);
        }

        [TestMethod]
        public void Elegir_SinClavePrivada_NoSeElige()
        {
            // Un .cer (parte pública) importado en el almacén no sirve para autenticarse.
            CandidatoCertificado sinClave = Candidato(Ahora.AddYears(-1), Ahora.AddYears(1), clavePrivada: false);

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { sinClave }, Ahora, NIF_EMPRESA);

            Assert.IsNull(elegido);
        }

        [TestMethod]
        public void Elegir_DeOtraEmpresa_NoSeElige()
        {
            // En LocalMachine\My conviven certificados de IIS, RDP... no hay que cogerlos.
            CandidatoCertificado otro = Candidato(Ahora.AddYears(-1), Ahora.AddYears(1),
                subject: "CN=RDS2016.NUEVAVISION.LOCAL");
            CandidatoCertificado otraEmpresa = Candidato(Ahora.AddYears(-1), Ahora.AddYears(1),
                subject: "C=ES, O=OTRA SA, OID.2.5.4.97=VATES-B12345678, CN=OTRO (R: B12345678)");

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { otro, otraEmpresa }, Ahora, NIF_EMPRESA);

            Assert.IsNull(elegido);
        }

        [TestMethod]
        public void Elegir_SubjectSoloConMarcadorDeRepresentante_SeAcepta()
        {
            // Algunos emisores no incluyen el organizationIdentifier VATES-; basta el (R: NIF).
            CandidatoCertificado soloR = Candidato(Ahora.AddYears(-1), Ahora.AddYears(1),
                subject: "C=ES, CN=00000000T CARLOS ADRIAN (R: A78368255)");

            CandidatoCertificado elegido = ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado> { soloR }, Ahora, NIF_EMPRESA);

            Assert.AreSame(soloR, elegido);
        }

        [TestMethod]
        public void Elegir_SinCandidatos_DevuelveNull()
        {
            Assert.IsNull(ProveedorCertificadoAeat.Elegir(
                new List<CandidatoCertificado>(), Ahora, NIF_EMPRESA));
            Assert.IsNull(ProveedorCertificadoAeat.Elegir(null, Ahora, NIF_EMPRESA));
        }

        [TestMethod]
        public void EsDeLaEmpresa_IgnoraMayusculasYEspacios()
        {
            Assert.IsTrue(ProveedorCertificadoAeat.EsDeLaEmpresa(
                "oid.2.5.4.97=vates-a78368255", " a78368255 "));
            Assert.IsFalse(ProveedorCertificadoAeat.EsDeLaEmpresa(
                "CN=cualquier cosa", NIF_EMPRESA));
            Assert.IsFalse(ProveedorCertificadoAeat.EsDeLaEmpresa(null, NIF_EMPRESA));
        }
    }
}
