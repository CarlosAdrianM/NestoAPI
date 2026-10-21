using System;
using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
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
    public class ReembolsoContadoMantenerJuntoTests
    {
        private const string EMPRESA = "1";
        private const int PEDIDO = 928220;
        private static readonly DateTime ALBARAN_13_10 = new DateTime(2026, 10, 13, 11, 43, 49);

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

        private static LinPedidoVta Albaran(decimal total)
            => new LinPedidoVta { Número = PEDIDO, Estado = Constantes.EstadosLineaVenta.ALBARAN, Picking = 99806, Total = total, Fecha_Albarán = ALBARAN_13_10, Nº_Factura = null };

        /// <summary>Lo que había en el pedido al hacer la etiqueta del 19/10: el albarán 730977 sin facturar y el 45958 con picking.</summary>
        private static List<LinPedidoVta> Lineas928220Al19Oct() => new List<LinPedidoVta>
        {
            Albaran(214.17m), Albaran(0m), Albaran(48.158m), Albaran(0m), Albaran(11.979m), Albaran(19.6625m),
            Albaran(0m), Albaran(0m), Albaran(0m), Albaran(3.63m),
            new LinPedidoVta { Número = PEDIDO, Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 99864, Total = 28.193m }
        };

        private static IServicioPedidosVenta ServicioSinEfectos()
        {
            var servicio = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicio.CargarEfectosPedido(A<string>._, A<int>._)).Returns(new List<EfectoPedidoVenta>());
            A.CallTo(() => servicio.ReembolsosCobradosDelPedido(A<string>._, A<int>._, A<DateTime>._)).Returns(0m);
            return servicio;
        }

        // ------------------------------------------------------------ reembolso

        [TestMethod]
        public void ImporteReembolso_928220_ElEnvioQueCompletaElPedidoCobraTodaLaFactura()
        {
            // Regresión #686: salía 28,19 € (solo su albarán) y la factura era de 325,79 €.
            decimal reembolso = GestorEnviosAgencia.ImporteReembolso(Cabecera928220(), Lineas928220Al19Oct(), ServicioSinEfectos());

            Assert.AreEqual(325.79m, reembolso);
        }

        [TestMethod]
        public void ImporteReembolso_928220_DesdeLaBD_ComoElGetDeAgencias()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            List<LinPedidoVta> lineas = Lineas928220Al19Oct();
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(Cabecera928220());
            A.CallTo(() => servicio.CargarLineasPedidoPendientes(PEDIDO)).Returns(new List<LinPedidoVta>());
            A.CallTo(() => servicio.CargarLineasPedidoSinPicking(PEDIDO)).Returns(lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO).ToList());
            A.CallTo(() => servicio.CargarLineasAlbaranSinFacturar(PEDIDO)).Returns(lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN).ToList());

            Assert.AreEqual(325.79m, new GestorPedidosVenta(servicio).ImporteReembolso(EMPRESA, PEDIDO));
        }

        [TestMethod]
        public void ImporteReembolso_MantenerJunto_DescuentaLoYaCobradoEnLosEnviosDeEsosAlbaranes()
        {
            // Si alguien cobró algo con el primer envío (reembolso a mano), no se cobra dos veces.
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            A.CallTo(() => servicio.ReembolsosCobradosDelPedido("1", PEDIDO, ALBARAN_13_10.Date)).Returns(100m);

            decimal reembolso = GestorEnviosAgencia.ImporteReembolso(Cabecera928220(), Lineas928220Al19Oct(), servicio);

            Assert.AreEqual(225.79m, reembolso);
        }

        [TestMethod]
        public void ImporteReembolso_MantenerJunto_AlbaranesYaFacturados_NoCuentan()
        {
            List<LinPedidoVta> lineas = Lineas928220Al19Oct();
            foreach (LinPedidoVta linea in lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN))
            {
                linea.Nº_Factura = "NV2616000 ";
            }

            Assert.AreEqual(28.19m, GestorEnviosAgencia.ImporteReembolso(Cabecera928220(), lineas, ServicioSinEfectos()));
        }

        [TestMethod]
        public void ImporteReembolso_SinMantenerJunto_SoloLoQueSaleEnEsteEnvio()
        {
            // Por entregas cada albarán lleva su factura y su cobro: no se toca.
            decimal reembolso = GestorEnviosAgencia.ImporteReembolso(Cabecera928220(mantenerJunto: false), Lineas928220Al19Oct(), ServicioSinEfectos());

            Assert.AreEqual(28.19m, reembolso);
        }

        [TestMethod]
        public void ImporteReembolso_TodoAhora_ElEnvioQueFacturaCobraTodoLoQueQuedaSinFacturar()
        {
            // Decisión de Carlos (21/10/26): «Todo ahora» (modo 3) al contado no se bloquea, pero el envío cuyo albarán
            // factura el pedido entero cobra toda la factura, también lo que quede en albarán sin facturar.
            CabPedidoVta cabecera = Cabecera928220(mantenerJunto: false);
            cabecera.ModoFacturacion = M.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

            Assert.AreEqual(325.79m, GestorEnviosAgencia.ImporteReembolso(cabecera, Lineas928220Al19Oct(), ServicioSinEfectos()));
        }

        [TestMethod]
        public void ImporteReembolso_TodoAhora_DescuentaLoYaCobrado()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            A.CallTo(() => servicio.ReembolsosCobradosDelPedido("1", PEDIDO, ALBARAN_13_10.Date)).Returns(297.60m);
            CabPedidoVta cabecera = Cabecera928220(mantenerJunto: false);
            cabecera.ModoFacturacion = M.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;

            Assert.AreEqual(28.19m, GestorEnviosAgencia.ImporteReembolso(cabecera, Lineas928220Al19Oct(), servicio));
        }

        [TestMethod]
        public void ImporteReembolso_TodoAhora_DesdeLaBD_CargaLosAlbaranesSinFacturar()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            List<LinPedidoVta> lineas = Lineas928220Al19Oct();
            CabPedidoVta cabecera = Cabecera928220(mantenerJunto: false);
            cabecera.ModoFacturacion = M.TODO_AHORA_Y_LO_PENDIENTE_DESPUES;
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(cabecera);
            A.CallTo(() => servicio.CargarLineasPedidoPendientes(PEDIDO)).Returns(new List<LinPedidoVta>());
            A.CallTo(() => servicio.CargarLineasPedidoSinPicking(PEDIDO)).Returns(lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO).ToList());
            A.CallTo(() => servicio.CargarLineasAlbaranSinFacturar(PEDIDO)).Returns(lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.ALBARAN).ToList());

            Assert.AreEqual(325.79m, new GestorPedidosVenta(servicio).ImporteReembolso(EMPRESA, PEDIDO));
        }

        [TestMethod]
        public void ImporteReembolso_PrimerEnvioConLineasPendientes_SigueSiendoCero_PeroSeAvisa()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            var lineas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 99806, Total = 297.60m },
                new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.PENDIENTE, Picking = 0, Total = 28.19m }
            };

            Assert.AreEqual(0m, GestorEnviosAgencia.ImporteReembolso(Cabecera928220(), lineas, servicio));
            Assert.IsTrue(GestorEnviosAgencia.SaleSinReembolsoPorMantenerJunto(Cabecera928220(),
                lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.PENDIENTE), servicio));
        }

        [TestMethod]
        public void AvisoReembolso_928220El13Oct_AvisaParaAgenciasDeNesto()
        {
            // El GET api/PedidosVenta/AvisoReembolso que pregunta Agencias al insertar un envío sin reembolso.
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            A.CallTo(() => servicio.LeerCabPedidoVta(EMPRESA, PEDIDO)).Returns(Cabecera928220());
            A.CallTo(() => servicio.CargarLineasPedidoPendientes(PEDIDO)).Returns(new List<LinPedidoVta>
            {
                new LinPedidoVta { Número = PEDIDO, Estado = Constantes.EstadosLineaVenta.PENDIENTE, Total = 28.193m }
            });
            var gestor = new GestorPedidosVenta(servicio);

            Assert.AreEqual(MantenerJuntoContado.AVISO_ENVIO_SIN_REEMBOLSO, gestor.AvisoReembolsoMantenerJunto(EMPRESA, PEDIDO));

            A.CallTo(() => servicio.CargarLineasPedidoPendientes(PEDIDO)).Returns(new List<LinPedidoVta>());
            Assert.IsNull(gestor.AvisoReembolsoMantenerJunto(EMPRESA, PEDIDO), "El 19/10 ya no quedaba nada pendiente: se cobra todo");
        }

        [TestMethod]
        public void SaleSinReembolsoPorMantenerJunto_NoEsContadoOSinPendientes_False()
        {
            IServicioPedidosVenta servicio = ServicioSinEfectos();
            var pendientes = new List<LinPedidoVta> { new LinPedidoVta { Estado = Constantes.EstadosLineaVenta.PENDIENTE } };

            Assert.IsFalse(GestorEnviosAgencia.SaleSinReembolsoPorMantenerJunto(Cabecera928220(formaPago: "TRN"), pendientes, servicio), "Transferencia: no se cobra en la entrega");
            Assert.IsFalse(GestorEnviosAgencia.SaleSinReembolsoPorMantenerJunto(Cabecera928220(), new List<LinPedidoVta>(), servicio), "Sin pendientes ya se cobra todo");
            Assert.IsFalse(GestorEnviosAgencia.SaleSinReembolsoPorMantenerJunto(Cabecera928220(mantenerJunto: false), pendientes, servicio));
        }
    }
}
