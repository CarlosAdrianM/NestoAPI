using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias.Perfiles;
using NestoAPI.Models;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#258 (Fase 3): las reglas de destino (#204) y los defaults de envío viven en los
    /// perfiles de agencia, no en switch del controller. Las de CEX/Sending aplican aunque estén
    /// en cuarentena (registro sin puerta).
    /// </summary>
    [TestClass]
    public class PerfilesAgenciasTests
    {
        private const string CP_CANARIAS = "35001";
        private const string CP_MADRID = "28001";

        private static CabPedidoVta PedidoConBase(decimal baseImponible) => new CabPedidoVta
        {
            LinPedidoVtas = new List<LinPedidoVta>
            {
                new LinPedidoVta { TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Base_Imponible = baseImponible }
            }
        };

        [TestMethod]
        public void Canteras_FueraDeCanarias_Rechaza()
        {
            string error = new PerfilAgenciaCanteras().ValidarDestino(CP_MADRID, PedidoConBase(1000), cobrarReembolso: false);
            StringAssert.Contains(error, "solo opera en Canarias");
        }

        [TestMethod]
        public void Canteras_ConReembolso_Rechaza()
        {
            string error = new PerfilAgenciaCanteras().ValidarDestino(CP_CANARIAS, PedidoConBase(1000), cobrarReembolso: true);
            StringAssert.Contains(error, "no admite contra reembolso");
        }

        [TestMethod]
        public void Canteras_SinLlegarAlMinimoYSinPortes_Rechaza()
        {
            decimal pocoImporte = NestoAPI.Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS - 1;
            string error = new PerfilAgenciaCanteras().ValidarDestino(CP_CANARIAS, PedidoConBase(pocoImporte), cobrarReembolso: false);
            StringAssert.Contains(error, "no llega al mínimo de Canarias");
        }

        [TestMethod]
        public void Canteras_CanariasConImporteSuficiente_Acepta()
        {
            decimal importeOk = NestoAPI.Models.Picking.GestorImportesMinimos.IMPORTE_MINIMO_CANARIAS;
            Assert.IsNull(new PerfilAgenciaCanteras().ValidarDestino(CP_CANARIAS, PedidoConBase(importeOk), cobrarReembolso: false));
        }

        [TestMethod]
        public void CorreosExpress_Canarias_RechazaYRedirigeACanteras()
        {
            string error = new PerfilAgenciaCorreosExpress().ValidarDestino(CP_CANARIAS, PedidoConBase(1000), cobrarReembolso: false);
            StringAssert.Contains(error, "Canteras");
        }

        [TestMethod]
        public void CorreosExpress_Peninsula_Acepta()
        {
            Assert.IsNull(new PerfilAgenciaCorreosExpress().ValidarDestino(CP_MADRID, PedidoConBase(1000), cobrarReembolso: false));
        }

        [TestMethod]
        public void DefaultsEnvio_PorAgenciaYCodigoPostal()
        {
            Assert.AreEqual(((short)96, (short)18, 34), new PerfilAgenciaGls().DefaultsEnvio(CP_MADRID));
            Assert.AreEqual(((short)1, (short)1, 34), new PerfilAgenciaSending().DefaultsEnvio(CP_MADRID));
            var cex = new PerfilAgenciaCorreosExpress();
            Assert.AreEqual(((short)93, (short)0, 724), cex.DefaultsEnvio(CP_MADRID), "ePaq24 España");
            Assert.AreEqual(((short)63, (short)0, 724), cex.DefaultsEnvio("1000-001"), "Paq24 Portugal");
            Assert.AreEqual(((short)90, (short)0, 724), cex.DefaultsEnvio("75008"), "CP francés: internacional monobulto");
        }

        [TestMethod]
        public void RegistroSinPuerta_IncluyeLasAgenciasEnCuarentena()
        {
            // La cuarentena gobierna la tramitación remota; las reglas de destino y los defaults
            // aplican igual a CEX y Sending (comportamiento de los antiguos switch del controller).
            RegistroAgencias registro = RegistroAgencias.PorReflexionSinPuerta();

            Assert.IsNotNull(registro.Perfil(Constantes.Agencias.AGENCIA_CORREOS_EXPRESS));
            Assert.IsNotNull(registro.Perfil(Constantes.Agencias.AGENCIA_SENDING));
            Assert.IsNotNull(registro.Perfil(Constantes.Agencias.AGENCIA_CANTERAS));
            Assert.IsInstanceOfType(registro.Perfil(Constantes.Agencias.AGENCIA_CANTERAS), typeof(IPerfilConReglasDestino));
        }
    }
}
