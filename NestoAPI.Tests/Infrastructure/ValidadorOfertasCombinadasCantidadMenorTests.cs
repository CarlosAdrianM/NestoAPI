using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Casilla "Permitir cantidad menor" por línea (NestoAPI#239). Con el flag, la Cantidad de la
    /// línea pasa a ser un MÁXIMO: el pedido puede llevar de 0 a Cantidad de ese producto sin que la
    /// oferta deje de validar; llevar MÁS sigue sin permitirse. Modela la oferta real
    /// "Level Lash Sérum 6+2" (sérum obligatorio + 20 folletos + 1 expositor como extras opcionales).
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasCantidadMenorTests
    {
        private const string SERUM = "SERUM";
        private const string FOLLETO = "FOLLETO";
        private const string EXPOSITOR = "EXPOSITOR";

        private IServicioPrecios _servicio;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();
        }

        // Sérum obligatorio (precio mínimo 10) + 20 folletos y 1 expositor a precio 0 con
        // "permitir cantidad menor" marcado. Sin importe mínimo: match directo.
        private OfertaCombinada CrearOfertaSerum()
        {
            return new OfertaCombinada
            {
                Id = 300,
                Empresa = "1",
                ImporteMinimo = 0,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = SERUM, Precio = 10, Cantidad = 1, PermitirCantidadMenor = false },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = FOLLETO, Precio = 0, Cantidad = 20, PermitirCantidadMenor = true },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = EXPOSITOR, Precio = 0, Cantidad = 1, PermitirCantidadMenor = true }
                }
            };
        }

        private void FakeOferta(OfertaCombinada oferta, params string[] productos)
        {
            var lista = new List<OfertaCombinada> { oferta };
            foreach (string p in productos)
            {
                A.CallTo(() => _servicio.BuscarOfertasCombinadas(p)).Returns(lista);
            }
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                AplicarDescuento = true
            };
        }

        private static PedidoVentaDTO CrearPedido(params LineaPedidoVentaDTO[] lineas)
        {
            PedidoVentaDTO pedido = A.Fake<PedidoVentaDTO>();
            pedido.cliente = "5";
            pedido.contacto = "0";
            foreach (var l in lineas)
            {
                pedido.Lineas.Add(l);
            }
            return pedido;
        }

        [TestMethod]
        public void Folletos_MenosDeLoIndicado_EsValido()
        {
            // 10 folletos en vez de 20: con "permitir cantidad menor", debe validar.
            OfertaCombinada oferta = CrearOfertaSerum();
            FakeOferta(oferta, SERUM, FOLLETO, EXPOSITOR);
            PedidoVentaDTO pedido = CrearPedido(Linea(SERUM, 1, 10), Linea(FOLLETO, 10, 0), Linea(EXPOSITOR, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsTrue(validador.EsPedidoValido(pedido, FOLLETO, _servicio).ValidacionSuperada,
                "10 folletos (de 20 máx.) deben validar la oferta");
        }

        [TestMethod]
        public void Expositor_Cero_EsValido()
        {
            // El cliente ya tiene expositor y pone 0: con el flag, 0 es válido (0..1).
            OfertaCombinada oferta = CrearOfertaSerum();
            FakeOferta(oferta, SERUM, FOLLETO, EXPOSITOR);
            PedidoVentaDTO pedido = CrearPedido(Linea(SERUM, 1, 10), Linea(FOLLETO, 20, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsTrue(validador.EsPedidoValido(pedido, FOLLETO, _servicio).ValidacionSuperada,
                "0 expositores (de 1 máx.) deben validar la oferta");
        }

        [TestMethod]
        public void Folletos_Y_Expositor_ATope_EsValido()
        {
            // Llevar exactamente el máximo (20 folletos + 1 expositor) sigue siendo válido.
            OfertaCombinada oferta = CrearOfertaSerum();
            FakeOferta(oferta, SERUM, FOLLETO, EXPOSITOR);
            PedidoVentaDTO pedido = CrearPedido(Linea(SERUM, 1, 10), Linea(FOLLETO, 20, 0), Linea(EXPOSITOR, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsTrue(validador.EsPedidoValido(pedido, FOLLETO, _servicio).ValidacionSuperada,
                "El máximo exacto debe seguir validando");
        }

        [TestMethod]
        public void Folletos_MasDeLoIndicado_NoEsValido()
        {
            // 25 folletos superan el máximo de 20: la oferta no debe autorizarlos.
            OfertaCombinada oferta = CrearOfertaSerum();
            FakeOferta(oferta, SERUM, FOLLETO, EXPOSITOR);
            PedidoVentaDTO pedido = CrearPedido(Linea(SERUM, 1, 10), Linea(FOLLETO, 25, 0), Linea(EXPOSITOR, 1, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, FOLLETO, _servicio).ValidacionSuperada,
                "25 folletos superan el máximo (20): no debe validar");
        }

        [TestMethod]
        public void SinProductoObligatorio_NoEsValido()
        {
            // Aunque los extras tengan "cantidad menor", el sérum obligatorio sigue siendo necesario.
            OfertaCombinada oferta = CrearOfertaSerum();
            FakeOferta(oferta, SERUM, FOLLETO, EXPOSITOR);
            PedidoVentaDTO pedido = CrearPedido(Linea(FOLLETO, 10, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, FOLLETO, _servicio).ValidacionSuperada,
                "Sin el sérum obligatorio la oferta no se autoriza");
        }

        [TestMethod]
        public void SinFlag_ExigeCantidadExacta_Regresion()
        {
            // Sin marcar "permitir cantidad menor" (comportamiento por defecto): 10 folletos de 20
            // requeridos NO validan, como hasta ahora.
            var oferta = new OfertaCombinada
            {
                Id = 301,
                Empresa = "1",
                ImporteMinimo = 0,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 301, Empresa = "1", Producto = SERUM, Precio = 10, Cantidad = 1, PermitirCantidadMenor = false },
                    new OfertaCombinadaDetalle { OfertaId = 301, Empresa = "1", Producto = FOLLETO, Precio = 0, Cantidad = 20, PermitirCantidadMenor = false }
                }
            };
            FakeOferta(oferta, SERUM, FOLLETO);
            PedidoVentaDTO pedido = CrearPedido(Linea(SERUM, 1, 10), Linea(FOLLETO, 10, 0));

            var validador = new ValidadorOfertasCombinadas();

            Assert.IsFalse(validador.EsPedidoValido(pedido, FOLLETO, _servicio).ValidacionSuperada,
                "Sin el flag, 10 de 20 folletos no debe validar (comportamiento previo)");
        }
    }
}
