using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Remesas;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#368: decisión sobre secuencias (FRST/RCUR) incoherentes entre contactos que
    /// comparten CCC. Caso real: cliente 40652 reorganizó contactos y el chequeo GLOBAL del SP
    /// bloqueó todas las remesas dos días (18 errores en ELMAH, sin mensaje para el usuario).
    /// </summary>
    [TestClass]
    public class ResolutorSecuenciasCccTests
    {
        private static CccSecuencia Fila(string cliente, string contacto, string secuencia,
            string ccc = "1", string cuenta = "ES12100420001234567890", string empresa = "1")
        {
            return new CccSecuencia
            {
                Empresa = empresa,
                Cliente = cliente,
                Ccc = ccc,
                Contacto = contacto,
                Secuencia = secuencia,
                CuentaBancaria = cuenta
            };
        }

        [TestMethod]
        public void Resolver_FrstYRcurConLaMismaCuenta_UnificaARcur()
        {
            var resolucion = ResolutorSecuenciasCcc.Resolver(new List<CccSecuencia>
            {
                Fila("40652", "1", "RCUR"),
                Fila("40652", "2", "FRST")
            });

            Assert.AreEqual(0, resolucion.Errores.Count);
            Assert.AreEqual(1, resolucion.UnificarARcur.Count);
            Assert.AreEqual("40652", resolucion.UnificarARcur[0].Cliente);
            Assert.AreEqual("1", resolucion.UnificarARcur[0].Ccc);
        }

        [TestMethod]
        public void Resolver_CuentasBancariasDistintasBajoElMismoCcc_EsErrorConDetalle()
        {
            // No se unifica a ciegas: si los IBAN difieren NO es el mismo mandato.
            var resolucion = ResolutorSecuenciasCcc.Resolver(new List<CccSecuencia>
            {
                Fila("40652", "1", "RCUR", cuenta: "ES1111111111111111111111"),
                Fila("40652", "2", "FRST", cuenta: "ES2222222222222222222222")
            });

            Assert.AreEqual(0, resolucion.UnificarARcur.Count);
            Assert.AreEqual(1, resolucion.Errores.Count);
            StringAssert.Contains(resolucion.Errores[0], "40652");
            StringAssert.Contains(resolucion.Errores[0], "cuentas bancarias distintas");
        }

        [TestMethod]
        public void Resolver_SecuenciaNoReconocida_EsErrorConDetalle()
        {
            var resolucion = ResolutorSecuenciasCcc.Resolver(new List<CccSecuencia>
            {
                Fila("39416", "2", "FRST"),
                Fila("39416", "3", "OOFF")
            });

            Assert.AreEqual(0, resolucion.UnificarARcur.Count);
            Assert.AreEqual(1, resolucion.Errores.Count);
            StringAssert.Contains(resolucion.Errores[0], "39416");
            StringAssert.Contains(resolucion.Errores[0], "OOFF");
        }

        [TestMethod]
        public void Resolver_VariosGrupos_CadaUnoASuLista()
        {
            var resolucion = ResolutorSecuenciasCcc.Resolver(new List<CccSecuencia>
            {
                Fila("40652", "1", "RCUR"),
                Fila("40652", "2", "FRST"),
                Fila("39416", "2", "FRST", ccc: "2"),
                Fila("39416", "3", "OOFF", ccc: "2")
            });

            Assert.AreEqual(1, resolucion.UnificarARcur.Count);
            Assert.AreEqual("40652", resolucion.UnificarARcur[0].Cliente);
            Assert.AreEqual(1, resolucion.Errores.Count);
            StringAssert.Contains(resolucion.Errores[0], "39416");
        }

        [TestMethod]
        public void Resolver_SecuenciasIgualesTrasTrim_NoHaceNada()
        {
            // La tabla ccc rellena con espacios (legacy): "RCUR " y "RCUR" son la misma.
            var resolucion = ResolutorSecuenciasCcc.Resolver(new List<CccSecuencia>
            {
                Fila("40652", "1", "RCUR "),
                Fila("40652", "2", "RCUR")
            });

            Assert.AreEqual(0, resolucion.UnificarARcur.Count);
            Assert.AreEqual(0, resolucion.Errores.Count);
        }

        [TestMethod]
        public void Resolver_SinFilas_NoHaceNada()
        {
            var resolucion = ResolutorSecuenciasCcc.Resolver(new List<CccSecuencia>());

            Assert.AreEqual(0, resolucion.UnificarARcur.Count);
            Assert.AreEqual(0, resolucion.Errores.Count);
        }

        [TestMethod]
        public void EsErrorDeSecuencias_DetectaElRaiserrorDelSp()
        {
            Assert.IsTrue(ResolutorSecuenciasCcc.EsErrorDeSecuencias(
                "Los contactos 2 y 1 del cliente 40652 tienen secuencias diferentes."));
            Assert.IsFalse(ResolutorSecuenciasCcc.EsErrorDeSecuencias("Timeout expired"));
            Assert.IsFalse(ResolutorSecuenciasCcc.EsErrorDeSecuencias(null));
        }
    }
}
