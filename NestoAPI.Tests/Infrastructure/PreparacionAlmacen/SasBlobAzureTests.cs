using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.PreparacionAlmacen
{
    /// <summary>
    /// NestoAPI#556: los enlaces firmados de las fotos de los bultos. La firma está hecha a mano (sin
    /// el SDK de Azure), así que aquí se fija contra un valor calculado aparte: si alguien cambia el
    /// orden de los campos o la versión, lo dice este test antes que Azure con un 403.
    /// </summary>
    [TestClass]
    public class SasBlobAzureTests
    {
        // Una clave inventada (Base64 de un texto cualquiera): no es de ninguna cuenta
        private const string CLAVE = "Y2xhdmUtZGUtcHJ1ZWJhLXF1ZS1uby1lcy1kZS1uYWRpZS0wMTIzNDU2Nzg5";
        private const string RUTA = "1/926940/99633/bulto-1-20260930120000.jpg";
        private static readonly DateTime DESDE = new DateTime(2026, 9, 30, 9, 55, 0, DateTimeKind.Utc);
        private static readonly DateTime CADUCA = new DateTime(2026, 9, 30, 10, 15, 0, DateTimeKind.Utc);

        [TestMethod]
        public void Firmar_CoincideConElValorCalculadoAparte()
        {
            string cadena = SasBlobAzure.CadenaAFirmar("cuentaprueba", "bultos", RUTA, "r", DESDE, CADUCA);

            // HMAC-SHA256 calculado con otra herramienta sobre la misma cadena y la misma clave
            Assert.AreEqual("dbAuUt1B5XwZtO3FcOsxdVR+FeIvxKyauFH8GhuxmwY=", SasBlobAzure.Firmar(CLAVE, cadena));
        }

        [TestMethod]
        public void CadenaAFirmar_TieneLosDieciseisCamposDeLaVersion()
        {
            string[] campos = SasBlobAzure.CadenaAFirmar("cuentaprueba", "bultos", RUTA, "r", DESDE, CADUCA).Split('\n');

            Assert.AreEqual(16, campos.Length);
            Assert.AreEqual("r", campos[0]);
            Assert.AreEqual("2026-09-30T09:55:00Z", campos[1]);
            Assert.AreEqual("2026-09-30T10:15:00Z", campos[2]);
            Assert.AreEqual("/blob/cuentaprueba/bultos/" + RUTA, campos[3]);
            Assert.AreEqual("https", campos[6]);
            Assert.AreEqual(SasBlobAzure.VERSION_SERVICIO, campos[7]);
            Assert.AreEqual("b", campos[8]);
        }

        [TestMethod]
        public void UrlFirmada_ApuntaAlBlobYLlevaLaFirmaCodificada()
        {
            var cuenta = new SasBlobAzure.CuentaAlmacenamiento { Nombre = "cuentaprueba", Clave = CLAVE };

            Uri url = SasBlobAzure.UrlFirmada(cuenta, "bultos", RUTA, "r", CADUCA, DESDE);

            Assert.AreEqual("https://cuentaprueba.blob.core.windows.net/bultos/" + RUTA, url.GetLeftPart(UriPartial.Path));
            StringAssert.Contains(url.Query, "sp=r");
            StringAssert.Contains(url.Query, "sr=b");
            StringAssert.Contains(url.Query, "se=2026-09-30T10%3A15%3A00Z");
            // El «+» y el «=» de la firma tienen que ir codificados o Azure lee otra firma
            StringAssert.Contains(url.Query, "sig=dbAuUt1B5XwZtO3FcOsxdVR%2BFeIvxKyauFH8GhuxmwY%3D");
        }

        [TestMethod]
        public void LeerCadenaDeConexion_LaClaveAcabadaEnIgual_NoSeCorta()
        {
            var cuenta = SasBlobAzure.LeerCadenaDeConexion(
                "DefaultEndpointsProtocol=https;AccountName=cuentaprueba;AccountKey=YWJjZA==;EndpointSuffix=core.windows.net");

            Assert.AreEqual("cuentaprueba", cuenta.Nombre);
            Assert.AreEqual("YWJjZA==", cuenta.Clave);
            Assert.AreEqual("https://cuentaprueba.blob.core.windows.net", cuenta.UrlBase);
        }

        [TestMethod]
        public void LeerCadenaDeConexion_VaciaOSoloLaClave_Null()
        {
            Assert.IsNull(SasBlobAzure.LeerCadenaDeConexion(null));
            Assert.IsNull(SasBlobAzure.LeerCadenaDeConexion("  "));
            // Pegar la clave suelta en vez de la cadena de conexión (pasó al configurarlo, 30/09/26)
            Assert.IsNull(SasBlobAzure.LeerCadenaDeConexion("YWJjZGVmZ2hpamtsbW5vcHFyc3R1dnd4eXo="));
        }

        [TestMethod]
        public void AlmacenFotos_SinCadenaDeConexion_NoEstaConfigurado()
        {
            var almacen = new AlmacenFotosBultosAzure(null, new HttpClient(), () => DateTime.UtcNow);

            Assert.IsFalse(almacen.Configurado);
            _ = Assert.ThrowsException<InvalidOperationException>(() => almacen.EnlaceDeLectura(RUTA, TimeSpan.FromMinutes(15)));
        }

        /// <summary>
        /// Contra la cuenta de verdad. Solo corre si la variable de entorno NESTO_TEST_FOTOS_BULTOS
        /// trae la cadena de conexión; en la suite normal sale como no concluyente. Sube un blob de
        /// prueba a la carpeta «_pruebas» y lo vuelve a leer con un enlace de lectura.
        /// </summary>
        [TestMethod]
        [TestCategory("Integracion")]
        public async Task Integracion_SubirYLeerConEnlacesFirmados()
        {
            string conexion = Environment.GetEnvironmentVariable("NESTO_TEST_FOTOS_BULTOS");
            if (string.IsNullOrWhiteSpace(conexion))
            {
                Assert.Inconclusive("Sin NESTO_TEST_FOTOS_BULTOS: no se prueba contra Azure.");
            }

            using (var cliente = new HttpClient())
            {
                var almacen = new AlmacenFotosBultosAzure(conexion, cliente, () => DateTime.UtcNow);
                byte[] contenido = { 0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4 };
                string ruta = $"_pruebas/prueba-{DateTime.UtcNow:yyyyMMddHHmmss}.jpg";

                await almacen.Subir(ruta, contenido, "image/jpeg");

                Uri lectura = almacen.EnlaceDeLectura(ruta, TimeSpan.FromMinutes(5));
                byte[] leido = await cliente.GetByteArrayAsync(lectura);
                CollectionAssert.AreEqual(contenido, leido);

                // Sin la firma, el blob no se puede leer: el contenedor es privado
                HttpResponseMessage sinFirma = await cliente.GetAsync(lectura.GetLeftPart(UriPartial.Path));
                Assert.IsFalse(sinFirma.IsSuccessStatusCode);
            }
        }
    }
}
