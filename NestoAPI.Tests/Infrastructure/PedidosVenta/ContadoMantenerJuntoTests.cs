using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using M = NestoAPI.Models.Constantes.Pedidos.ModosFacturacion;

namespace NestoAPI.Tests.Infrastructure.PedidosVenta
{
    /// <summary>
    /// NestoAPI#686 (pedido 928220, cliente 36915, EFC/CONTADO, MantenerJunto = 1): el envío 249695 (13/10, albarán 730977,
    /// 297,60 €) salió con reembolso 0 porque aún quedaba una línea pendiente; el 249890 (19/10, albarán 731403) salió
    /// con 28,19 € aunque con él se facturó todo (NV2616913, 325,79 €). Quedaron 297,60 € sin cobrar.
    /// </summary>
    [TestClass]
    public class ContadoMantenerJuntoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 928220;

        private static CabPedidoVta Cabecera928220(bool mantenerJunto = true, string formaPago = "EFC", string plazos = "CONTADO   ")
            => new CabPedidoVta
            {
                Empresa = "1  ",
                Número = PEDIDO,
                Nº_Cliente = "36915     ",
                Forma_Pago = formaPago,
                PlazosPago = plazos,
                Periodo_Facturacion = "NRM",
                CCC = null,
                MantenerJunto = mantenerJunto
            };

        // ------------------------------------------------------------ qué es «al contado»

        [TestMethod]
        public void EsContado_ContadoOCrContraReembolso()
        {
            Assert.IsTrue(MantenerJuntoContado.EsContado("EFC", "CONTADO   ", null, "NRM", false));
            Assert.IsTrue(MantenerJuntoContado.EsContado("EFC", "CR", null, "NRM", false));
            Assert.IsTrue(MantenerJuntoContado.EsContado("EFC", "CONTADO", "1", "NRM", false), "EFC no lleva CCC aunque la pantalla lo mande");
            Assert.IsFalse(MantenerJuntoContado.EsContado("TRN", "CONTADO", null, "NRM", false), "Transferencia: no se cobra en la entrega");
            Assert.IsFalse(MantenerJuntoContado.EsContado("TAR", "CONTADO", null, "NRM", false));
            Assert.IsFalse(MantenerJuntoContado.EsContado("RCB", "CONTADO", "1", "NRM", false), "Con CCC no es contra reembolso");
            Assert.IsFalse(MantenerJuntoContado.EsContado("EFC", "CONTADO", null, "FDM", false));
            Assert.IsFalse(MantenerJuntoContado.EsContado("EFC", "CONTADO", null, "NRM", true), "Nota de entrega");
            Assert.IsFalse(MantenerJuntoContado.EsContado("EFC", "30/60", null, "NRM", false), "Plazos que el trigger obliga a facturar de una vez");
        }

        // ------------------------------------------------------------ grabar el pedido

        private static PedidoVentaDTO PedidoDto(bool mantenerJunto, string formaPago = "EFC", string plazos = "CONTADO")
            => new PedidoVentaDTO { empresa = EMPRESA, numero = PEDIDO, formaPago = formaPago, plazosPago = plazos, periodoFacturacion = "NRM", iva = "G21", mantenerJunto = mantenerJunto };

        [TestMethod]
        public void AlCrear_ContadoConMantenerJunto_SeRechazaConElMotivo()
        {
            Assert.AreEqual(MantenerJuntoContado.MENSAJE, MantenerJuntoContado.MotivoRechazoAlCrear(PedidoDto(true)));
            Assert.IsNull(MantenerJuntoContado.MotivoRechazoAlCrear(PedidoDto(false)));
            Assert.IsNull(MantenerJuntoContado.MotivoRechazoAlCrear(PedidoDto(true, formaPago: "TRN")));
        }

        [TestMethod]
        public void AlModificar_UnPedidoQueYaEraAsi_NoSeRompe()
        {
            // 35 pedidos así en seis meses: editarlos sin tocar la casilla ni la forma de pago tiene que seguir funcionando.
            Assert.IsNull(MantenerJuntoContado.MotivoRechazoAlModificar(PedidoDto(true), Cabecera928220()));
        }

        [TestMethod]
        public void AlModificar_MarcarLaCasillaEnUnContado_SeRechaza()
        {
            Assert.AreEqual(MantenerJuntoContado.MENSAJE, MantenerJuntoContado.MotivoRechazoAlModificar(PedidoDto(true), Cabecera928220(mantenerJunto: false)));
        }

        [TestMethod]
        public void AlModificar_PasarAContadoUnPedidoConMantenerJunto_SeRechaza()
        {
            Assert.AreEqual(MantenerJuntoContado.MENSAJE,
                MantenerJuntoContado.MotivoRechazoAlModificar(PedidoDto(true), Cabecera928220(formaPago: "TRN")));
        }

        [TestMethod]
        public void Sugerir_ContadoConMantenerJunto_PorEntregasYAlCompletarNoSePuede()
        {
            ModosFacturacionPermitidos.Sugerencia sugerencia = ModosFacturacionPermitidos.Sugerir(PedidoDto(true), null, plazosSonDeLaFicha: true);

            Assert.AreEqual(M.POR_ENTREGAS, sugerencia.Modo);
            Assert.AreEqual(MantenerJuntoContado.MENSAJE, sugerencia.Motivo);
            ModoFacturacionPermitidoDTO alCompletar = sugerencia.Modos.Single(m => m.Modo == M.AL_COMPLETAR);
            Assert.IsFalse(alCompletar.Permitido);
            CollectionAssert.DoesNotContain(sugerencia.ModosPermitidos, M.AL_COMPLETAR);
        }

        [TestMethod]
        public void Sugerir_ContadoPorTransferencia_AlCompletarSigueSiendoPosible()
        {
            ModosFacturacionPermitidos.Sugerencia sugerencia = ModosFacturacionPermitidos.Sugerir(PedidoDto(true, formaPago: "TRN"), null, plazosSonDeLaFicha: true);

            Assert.AreEqual(M.AL_COMPLETAR, sugerencia.Modo);
        }

        // ------------------------------------------------------------ TiendasNuevaVision

        [TestMethod]
        public void PedidoDeLaApp_ContadoConMantenerJuntoEnLaFicha_NaceSinMantenerJunto()
        {
            // Si no, POST api/PedidosVenta lo rechazaría y el cliente no podría pedir desde la app.
            var ficha = new ClienteDTO { empresa = EMPRESA, cliente = "36915", contacto = "0", iva = "G21", periodoFacturacion = "NRM", ruta = "FW", mantenerJunto = true, vendedor = "JE" };
            var peticion = new PedidoClienteRequest { Lineas = new List<LineaPedidoClienteRequest>() };

            PedidoVentaDTO contado = ConstructorPedidoCliente.Construir(peticion, ficha, new Dictionary<string, ProductoPlantillaDTO>(),
                Constantes.FormasPago.EFECTIVO, Constantes.PlazosPago.CONTADO, new DateTime(2026, 10, 21));
            PedidoVentaDTO conTarjeta = ConstructorPedidoCliente.Construir(peticion, ficha, new Dictionary<string, ProductoPlantillaDTO>(),
                Constantes.FormasPago.TARJETA, Constantes.PlazosPago.PREPAGO, new DateTime(2026, 10, 21));

            Assert.IsFalse(contado.mantenerJunto);
            Assert.IsTrue(conTarjeta.mantenerJunto, "Con tarjeta prepago no se cobra en la entrega: se respeta la ficha");
        }
    }
}
