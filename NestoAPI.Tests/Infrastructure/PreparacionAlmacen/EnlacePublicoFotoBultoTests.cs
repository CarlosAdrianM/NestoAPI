using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: el enlace para enseñar la foto de un bulto a quien no tiene usuario.
    /// </summary>
    [TestClass]
    public class EnlacePublicoFotoBultoTests
    {
        private const string CLAVE = "clave-de-pruebas-de-mas-de-treinta-y-dos-caracteres";
        private static readonly Guid ID_CLIENTE = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        [TestMethod]
        public void Token_EsElNumeroDelBultoYUnaFirma_YSiempreElMismo()
        {
            string token = EnlacePublicoFotoBulto.Token(CLAVE, 17, ID_CLIENTE);

            StringAssert.StartsWith(token, "17-");
            Assert.AreEqual(3 + 40, token.Length);
            Assert.AreEqual(token, EnlacePublicoFotoBulto.Token(CLAVE, 17, ID_CLIENTE));
            Assert.AreEqual("api/Almacen/Fotos/" + token, EnlacePublicoFotoBulto.Ruta(CLAVE, 17, ID_CLIENTE));
        }

        [TestMethod]
        public void Token_SinClaveOConClaveCorta_NoHayEnlace()
        {
            Assert.IsNull(EnlacePublicoFotoBulto.Token(null, 17, ID_CLIENTE));
            Assert.IsNull(EnlacePublicoFotoBulto.Token("  ", 17, ID_CLIENTE));
            Assert.IsNull(EnlacePublicoFotoBulto.Token("corta", 17, ID_CLIENTE));
            Assert.IsNull(EnlacePublicoFotoBulto.Ruta("corta", 17, ID_CLIENTE));
            Assert.IsNull(EnlacePublicoFotoBulto.Token(CLAVE, 0, ID_CLIENTE));
        }

        [TestMethod]
        public void EsValido_ElTokenDelBulto_Si()
        {
            string token = EnlacePublicoFotoBulto.Token(CLAVE, 17, ID_CLIENTE);

            Assert.IsTrue(EnlacePublicoFotoBulto.TryLeerId(token, out int id));
            Assert.AreEqual(17, id);
            Assert.IsTrue(EnlacePublicoFotoBulto.EsValido(CLAVE, token, 17, ID_CLIENTE));
            Assert.IsTrue(EnlacePublicoFotoBulto.EsValido(CLAVE, token.ToUpperInvariant(), 17, ID_CLIENTE));
        }

        [TestMethod]
        public void EsValido_CambiandoElNumeroDeBulto_LaFirmaYaNoVale()
        {
            // Quien tiene el enlace del bulto 17 no puede ver el 18 cambiando el número
            string token = EnlacePublicoFotoBulto.Token(CLAVE, 17, ID_CLIENTE);
            string delVecino = "18" + token.Substring(2);

            Assert.IsTrue(EnlacePublicoFotoBulto.TryLeerId(delVecino, out int id));
            Assert.AreEqual(18, id);
            Assert.IsFalse(EnlacePublicoFotoBulto.EsValido(CLAVE, delVecino, 18, Guid.NewGuid()));
            Assert.IsFalse(EnlacePublicoFotoBulto.EsValido(CLAVE, delVecino, 18, ID_CLIENTE));
        }

        [TestMethod]
        public void EsValido_ConOtraClave_LosEnlacesRepartidosDejanDeValer()
        {
            string token = EnlacePublicoFotoBulto.Token(CLAVE, 17, ID_CLIENTE);

            Assert.IsFalse(EnlacePublicoFotoBulto.EsValido("otra-clave-distinta-tambien-de-mas-de-treinta-y-dos", token, 17, ID_CLIENTE));
            Assert.IsFalse(EnlacePublicoFotoBulto.EsValido(null, token, 17, ID_CLIENTE));
        }

        [TestMethod]
        public void TryLeerId_LoQueNoEsUnToken_No()
        {
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId(null, out _));
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId("", out _));
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId("17", out _));
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId("17-abc", out _));
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId("-" + new string('a', 40), out _));
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId("x7-" + new string('a', 40), out _));
            Assert.IsFalse(EnlacePublicoFotoBulto.TryLeerId("0-" + new string('a', 40), out _));
        }
    }
}
