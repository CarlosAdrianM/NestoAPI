using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.ChequesRegalo
{
    /// <summary>
    /// NestoAPI#808 (clienta 9238, pedido 929678, sugerencia 1003): al grabar o modificar un pedido que supera el mínimo del cheque
    /// regalo sin la línea del cheque, la respuesta avisa de que el cliente tiene un cheque sin usar. Nunca bloquea.
    /// </summary>
    [TestClass]
    public class AvisoChequeRegaloSinUsarTests
    {
        private const string PRODUCTO_CHEQUE = "CHEQUE50_OCT26";
        private const string CLIENTE = "9238";
        private const int PEDIDO = 929678;

        private const string MENSAJE = "El cliente tiene un cheque regalo de 50,00 € sin usar y este pedido supera los 250,00 € de producto: " +
            "añádalo desde el detalle del pedido si quiere aplicarlo.";

        private static ChequeRegaloClienteDTO Cheque(string estado = "Disponible") => new ChequeRegaloClienteDTO
        {
            Campana = "CHEQUE50_OCT_2026",
            Empresa = "1",
            Cliente = CLIENTE,
            Producto = PRODUCTO_CHEQUE,
            Importe = 50,
            MinimoCanje = 250,
            CanjeHasta = new DateTime(2026, 11, 7),
            PrefijosNombreExcluidosMinimo = new List<string> { "PACK 26" },
            GruposExcluidosMinimo = new List<string> { "PEL" },
            Estado = estado,
            SePuedeUsar = estado == "Disponible"
        };

        private static Dictionary<string, ProductoParaChequeRegalo> Productos() => RepositorioCanjeChequesRegalo.Diccionario(new[]
        {
            new ProductoParaChequeRegalo { Numero = PRODUCTO_CHEQUE, Nombre = "CHEQUE REGALO 50 €", Grupo = "COS", Ficticio = true, IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "COSM", Nombre = "CREMA FACIAL", Grupo = "COS", IvaRepercutido = "G21" },
            new ProductoParaChequeRegalo { Numero = "PELU", Nombre = "TINTE", Grupo = "PEL", IvaRepercutido = "G21" }
        });

        private static LinPedidoVta Linea(string producto, decimal baseImponible, short cantidad = 1) => new LinPedidoVta
        {
            Empresa = "1",
            Número = PEDIDO,
            TipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            Producto = producto,
            Cantidad = cantidad,
            Base_Imponible = baseImponible,
            Estado = Constantes.EstadosLineaVenta.EN_CURSO
        };

        private static readonly List<string> PRODUCTOS_CHEQUE = new List<string> { PRODUCTO_CHEQUE };

        // ---------------------------------------------------------------- la regla

        [TestMethod]
        public void Evaluar_ConChequeDisponibleYBaseSuperaElMinimo_SinLineaDeCheque_Avisa()
        {
            // 929678: base computable 256,06 € > 250 € y el cheque 542 disponible
            AvisoPedidoDTO aviso = AvisoChequeRegaloSinUsar.Evaluar(Cheque(),
                new[] { Linea("COSM", 256.06M) }, Productos(), PRODUCTOS_CHEQUE);

            Assert.IsNotNull(aviso);
            Assert.AreEqual(AvisoChequeRegaloSinUsar.TIPO, aviso.Tipo);
            Assert.AreEqual(MENSAJE, aviso.Mensaje);
        }

        [TestMethod]
        public void Evaluar_ConLaLineaDelCheque_NoAvisa()
        {
            AvisoPedidoDTO aviso = AvisoChequeRegaloSinUsar.Evaluar(Cheque(),
                new[] { Linea("COSM", 300M), Linea(PRODUCTO_CHEQUE + "  ", -50M, -1) }, Productos(), PRODUCTOS_CHEQUE);

            Assert.IsNull(aviso);
        }

        [TestMethod]
        public void Evaluar_SinCheque_NoAvisa()
        {
            Assert.IsNull(AvisoChequeRegaloSinUsar.Evaluar(null, new[] { Linea("COSM", 300M) }, Productos(), PRODUCTOS_CHEQUE));
        }

        [DataTestMethod]
        [DataRow("EnPedido")]
        [DataRow("Canjeado")]
        [DataRow("PendienteDeActivar")]
        [DataRow("Caducado")]
        public void Evaluar_ChequeQueNoSePuedeUsar_NoAvisa(string estado)
        {
            Assert.IsNull(AvisoChequeRegaloSinUsar.Evaluar(Cheque(estado), new[] { Linea("COSM", 300M) }, Productos(), PRODUCTOS_CHEQUE));
        }

        [TestMethod]
        public void Evaluar_BaseIgualAlMinimo_NoAvisa()
        {
            // Hay que SUPERAR el mínimo: 250,00 no vale (como el canje)
            Assert.IsNull(AvisoChequeRegaloSinUsar.Evaluar(Cheque(), new[] { Linea("COSM", 250M) }, Productos(), PRODUCTOS_CHEQUE));
        }

        [TestMethod]
        public void Evaluar_LoExcluidoDelMinimoNoSuma_NoAvisa()
        {
            // 200 de cosmética + 100 de PEL (excluido): la base computable es 200
            Assert.IsNull(AvisoChequeRegaloSinUsar.Evaluar(Cheque(), new[] { Linea("COSM", 200M), Linea("PELU", 100M) }, Productos(), PRODUCTOS_CHEQUE));
        }

        [TestMethod]
        public void Evaluar_LaLineaDeOtraCampana_CuentaComoLineaDeCheque()
        {
            Assert.IsNull(AvisoChequeRegaloSinUsar.Evaluar(Cheque(),
                new[] { Linea("COSM", 300M), Linea("CHEQUE50_FEB27", -50M, -1) }, Productos(), new List<string> { PRODUCTO_CHEQUE, "CHEQUE50_FEB27" }));
        }

        // ---------------------------------------------------------------- el avisador (lecturas)

        private static AvisadorChequeRegaloSinUsar Avisador(IServicioCanjeChequesRegalo servicio, IRepositorioCanjeChequesRegalo repositorio,
            List<LinPedidoVta> lineas)
            => new AvisadorChequeRegaloSinUsar(servicio, repositorio, (e, p) => Task.FromResult(lineas), () => new DateTime(2026, 10, 30, 10, 38, 54));

        [TestMethod]
        public async Task Aviso_ConChequeYPedidoQueSuperaElMinimo_Avisa()
        {
            var servicio = A.Fake<IServicioCanjeChequesRegalo>();
            var repositorio = A.Fake<IRepositorioCanjeChequesRegalo>();
            A.CallTo(() => servicio.LeerChequeVigente("1", CLIENTE, A<DateTime>._)).Returns(Cheque());
            A.CallTo(() => servicio.ProductosCheque()).Returns(PRODUCTOS_CHEQUE);
            A.CallTo(() => repositorio.LeerProductos("1", A<IEnumerable<string>>._)).Returns(Productos());

            AvisoPedidoDTO aviso = await Avisador(servicio, repositorio, new List<LinPedidoVta> { Linea("COSM", 256.06M) })
                .Aviso("1  ", PEDIDO, CLIENTE + "   ");

            Assert.AreEqual(MENSAJE, aviso?.Mensaje);
        }

        [TestMethod]
        public async Task Aviso_SiFallaLaLecturaDelCheque_SinAvisoYSinLanzar()
        {
            var servicio = A.Fake<IServicioCanjeChequesRegalo>();
            A.CallTo(() => servicio.LeerChequeVigente(A<string>._, A<string>._, A<DateTime>._)).Throws(new InvalidOperationException("BD caída"));

            AvisoPedidoDTO aviso = await Avisador(servicio, A.Fake<IRepositorioCanjeChequesRegalo>(), new List<LinPedidoVta> { Linea("COSM", 300M) })
                .Aviso("1", PEDIDO, CLIENTE);

            Assert.IsNull(aviso);
        }

        [TestMethod]
        public async Task Aviso_SinChequeDisponible_NoLeeNadaMas()
        {
            var servicio = A.Fake<IServicioCanjeChequesRegalo>();
            var repositorio = A.Fake<IRepositorioCanjeChequesRegalo>();
            A.CallTo(() => servicio.LeerChequeVigente(A<string>._, A<string>._, A<DateTime>._)).Returns(Task.FromResult<ChequeRegaloClienteDTO>(null));

            Assert.IsNull(await Avisador(servicio, repositorio, new List<LinPedidoVta> { Linea("COSM", 300M) }).Aviso("1", PEDIDO, CLIENTE));
            A.CallTo(() => repositorio.LeerProductos(A<string>._, A<IEnumerable<string>>._)).MustNotHaveHappened();
        }

        // ---------------------------------------------------------------- el controller

        [TestMethod]
        public async Task PedidosVenta_AvisoChequeRegaloSinUsar_SiElAvisadorLanza_SinAviso()
        {
            var avisador = A.Fake<IAvisadorChequeRegaloSinUsar>();
            A.CallTo(() => avisador.Aviso(A<string>._, A<int>._, A<string>._)).Throws(new InvalidOperationException("roto"));
            var controller = new PedidosVentaController(A.Fake<NVEntities>()) { AvisadorChequeSinUsar = avisador };

            Assert.IsNull(await controller.AvisoChequeRegaloSinUsar(new PedidoVentaDTO { empresa = "1", numero = PEDIDO, cliente = CLIENTE }));
        }

        [TestMethod]
        public async Task PedidosVenta_AvisoChequeRegaloSinUsar_DevuelveElDelAvisador()
        {
            var avisador = A.Fake<IAvisadorChequeRegaloSinUsar>();
            var esperado = new AvisoPedidoDTO { Tipo = AvisoChequeRegaloSinUsar.TIPO, Mensaje = MENSAJE };
            A.CallTo(() => avisador.Aviso("1", PEDIDO, CLIENTE)).Returns(esperado);
            var controller = new PedidosVentaController(A.Fake<NVEntities>()) { AvisadorChequeSinUsar = avisador };

            Assert.AreSame(esperado, await controller.AvisoChequeRegaloSinUsar(new PedidoVentaDTO { empresa = "1", numero = PEDIDO, cliente = CLIENTE }));
        }
    }
}
