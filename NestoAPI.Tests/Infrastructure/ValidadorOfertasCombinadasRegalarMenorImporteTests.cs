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
    /// Issue #290: 2+1 combinable entre referencias de PRECIOS DISTINTOS. Con RegalarMenorImporte
    /// la unidad a base 0 debe ser la de menor tarifa del conjunto y las pagadas deben cubrir su
    /// tarifa (suelo dinámico por combinación, imposible con el ImporteMinimo fijo).
    /// Caso: A=10€, B=12€, C=15€; oferta 2+1 con mezcla libre (grupos de alternativas).
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasRegalarMenorImporteTests
    {
        private const string PROD_A = "PROD_A"; // tarifa 10
        private const string PROD_B = "PROD_B"; // tarifa 12
        private const string PROD_C = "PROD_C"; // tarifa 15

        private IServicioPrecios _servicio;
        private ValidadorOfertasCombinadas _validador;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();
            _validador = new ValidadorOfertasCombinadas();
            A.CallTo(() => _servicio.BuscarProducto(PROD_A)).Returns(new Producto { Número = PROD_A, PVP = 10m });
            A.CallTo(() => _servicio.BuscarProducto(PROD_B)).Returns(new Producto { Número = PROD_B, PVP = 12m });
            A.CallTo(() => _servicio.BuscarProducto(PROD_C)).Returns(new Producto { Número = PROD_C, PVP = 15m });
        }

        // Oferta 2+1 mezclable: UN grupo de alternativas A/B/C con cantidad 3 (unidades totales,
        // combinables como se quiera) y sin restricción de precio por fila. Con
        // RegalarMenorImporte el propio motor exige que la unidad a base 0 sea la más barata y
        // que las pagadas cubran su tarifa (suelo dinámico) — no hace falta ImporteMinimo fijo.
        private OfertaCombinada CrearOferta2Mas1(bool regalarMenorImporte = true)
        {
            return new OfertaCombinada
            {
                Id = 300,
                Empresa = "1",
                ImporteMinimo = 0,
                RegalarMenorImporte = regalarMenorImporte,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = PROD_A, Precio = 0, Cantidad = 3, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = PROD_B, Precio = 0, Cantidad = 3, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = PROD_C, Precio = 0, Cantidad = 3, GrupoAlternativa = 1 }
                }
            };
        }

        private void FakeOferta(OfertaCombinada oferta)
        {
            var lista = new List<OfertaCombinada> { oferta };
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(PROD_A)).Returns(lista);
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(PROD_B)).Returns(lista);
            A.CallTo(() => _servicio.BuscarOfertasCombinadas(PROD_C)).Returns(lista);
        }

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, decimal descuento = 0)
        {
            return new LineaPedidoVentaDTO
            {
                Producto = producto,
                Cantidad = cantidad,
                PrecioUnitario = precio,
                DescuentoLinea = descuento,
                AplicarDescuento = false
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
        public void RegalarMenorImporte_PaganLasDosCarasYRegalanLaBarata_EsValido()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            // B y C pagadas a tarifa; A (la más barata) gratis.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_B, 1, 12m),
                Linea(PROD_C, 1, 15m),
                Linea(PROD_A, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void RegalarMenorImporte_RegalanLaCaraPagandoLasBaratas_SeRechaza()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            // A y B pagadas; C (la más cara) gratis → prohibido.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 1, 10m),
                Linea(PROD_B, 1, 12m),
                Linea(PROD_C, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "menor importe");
        }

        [TestMethod]
        public void RegalarMenorImporte_TresIguales_EsValido()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            // 3×C: 2 pagadas + 1 gratis. Empate de tarifa: vale.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 2, 15m),
                Linea(PROD_C, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void RegalarMenorImporte_PagadasConDescuento_SeRechazaPorElSueloDinamico()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            // B y C "pagadas" pero al 50 % de descuento: no cubren su tarifa → rechazo.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_B, 1, 12m, descuento: 0.5m),
                Linea(PROD_C, 1, 15m, descuento: 0.5m),
                Linea(PROD_A, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "tarifa");
        }

        [TestMethod]
        public void SinRegalarMenorImporte_RegalarLaCara_SigueValiendo()
        {
            // Regresión: con el flag a false, el comportamiento actual queda intacto (hay ofertas
            // reales que regalan a propósito un artículo más caro que lo comprado, p. ej. Lisap
            // regala un aparato de 130 € — por eso las ofertas EXISTENTES migran con false).
            OfertaCombinada oferta = CrearOferta2Mas1(regalarMenorImporte: false);
            FakeOferta(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 1, 10m),
                Linea(PROD_B, 1, 12m),
                Linea(PROD_C, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void RegalarMenorImporte_PagadasABajoPrecio_SeRechazaPorElSueloDinamico()
        {
            // Pregunta de Carlos (13/07): ¿pueden poner 2 a 1 € (tarifa 12/15) y la otra gratis?
            // No: el suelo dinámico exige cubrir la tarifa de las unidades no regaladas.
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_B, 1, 1m),
                Linea(PROD_C, 1, 1m),
                Linea(PROD_A, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "tarifa");
        }

        [TestMethod]
        public void RegalarMenorImporte_UnaPagadaDosGratis_SeRechaza()
        {
            // El 2+1 regala UNA unidad por oferta completa: pagar solo la cara y llevarse las
            // otras dos gratis debe rechazarse aunque lo pagado cubra su propia tarifa.
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 1, 15m),
                Linea(PROD_A, 1, 0m),
                Linea(PROD_B, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Con 1 pagada y 2 gratis no hay 2+1 que valga");
            StringAssert.Contains(respuesta.Motivo, "tarifa");
        }

        [TestMethod]
        public void RegalarMenorImporte_TodasGratis_SeRechaza()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 1, 0m),
                Linea(PROD_B, 1, 0m),
                Linea(PROD_C, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Las 3 gratis no las puede autorizar el 2+1");
        }

        [TestMethod]
        public void RegalarMenorImporte_DosInstancias_LasDosGratisSonLasDosMasBaratas()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            // 2×(2+1): pagadas 2×C + 2×B, gratis 2×A (las más baratas del conjunto) → válido.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 2, 15m),
                Linea(PROD_B, 2, 12m),
                Linea(PROD_A, 2, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        // ----- Issue #301: varias instancias mono-producto en el mismo pedido -----
        // Caso real (pedido 922350, 15/07/26): el vendedor mete el N+M de VARIAS referencias a la
        // vez, cada una con sus pagadas y su gratis (la gratis de cada instancia ES la más barata
        // de su instancia: son del mismo producto). El gate evaluaba el pool global y exigía que
        // todas las gratis fueran las más baratas del conjunto, tirando pedidos perfectos.

        [TestMethod]
        public void RegalarMenorImporte_VariasInstanciasMonoProducto_EsValido()
        {
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            // 3×(2+1) mono-producto: 2 pagadas a tarifa + 1 gratis de CADA referencia.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 2, 10m), Linea(PROD_A, 1, 0m),
                Linea(PROD_B, 2, 12m), Linea(PROD_B, 1, 0m),
                Linea(PROD_C, 2, 15m), Linea(PROD_C, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void RegalarMenorImporte_FiltroConVariasInstanciasMonoProducto_EsValido()
        {
            // Réplica del 922350 con fila de FILTRO (grupo/subgrupo, como la oferta real 260):
            // 3+1 de cada referencia del filtro, tarifas distintas.
            OfertaCombinada oferta = new OfertaCombinada
            {
                Id = 260,
                Empresa = "1",
                ImporteMinimo = 0,
                RegalarMenorImporte = true,
                UnidadesRegaladas = 1,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 260, Empresa = "1", Producto = null, Precio = 0, Cantidad = 4, Grupo = "COS", Subgrupo = "025" }
                }
            };
            FakeOferta(oferta);
            A.CallTo(() => _servicio.FiltrarLineas(A<PedidoVentaDTO>._, string.Empty, null, "COS", "025"))
                .ReturnsLazily((PedidoVentaDTO p, string filtro, string fam, string grupo, string subgrupo) =>
                    new List<LineaPedidoVentaDTO>(p.Lineas));

            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 3, 10m), Linea(PROD_A, 1, 0m),
                Linea(PROD_B, 3, 12m), Linea(PROD_B, 1, 0m),
                Linea(PROD_C, 3, 15m), Linea(PROD_C, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void RegalarMenorImporte_InstanciasCruzadasRegalandoLaCara_SigueRechazandose()
        {
            // Guard del abuso: 2 instancias pero las gratis son las 2 unidades CARAS (no hay
            // partición válida: ninguna instancia puede regalar C cobrando solo A y B).
            OfertaCombinada oferta = CrearOferta2Mas1();
            FakeOferta(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 2, 10m),
                Linea(PROD_B, 2, 12m),
                Linea(PROD_C, 2, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "menor importe");
        }

        // ----- Issue #292: UnidadesRegaladas por instancia (2+2, 3+2...) -----

        // Oferta N+M mezclable: un grupo de alternativas A/B/C con la cantidad TOTAL por
        // instancia (cobradas + regaladas) y UnidadesRegaladas = M en la cabecera.
        private OfertaCombinada CrearOfertaNMasM(short cantidadTotal, short unidadesRegaladas)
        {
            return new OfertaCombinada
            {
                Id = 300,
                Empresa = "1",
                ImporteMinimo = 0,
                RegalarMenorImporte = true,
                UnidadesRegaladas = unidadesRegaladas,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = PROD_A, Precio = 0, Cantidad = cantidadTotal, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = PROD_B, Precio = 0, Cantidad = cantidadTotal, GrupoAlternativa = 1 },
                    new OfertaCombinadaDetalle { OfertaId = 300, Empresa = "1", Producto = PROD_C, Precio = 0, Cantidad = cantidadTotal, GrupoAlternativa = 1 }
                }
            };
        }

        [TestMethod]
        public void UnidadesRegaladas_2Mas2_PaganLasDosCarasYRegalanLasDosBaratas_EsValido()
        {
            OfertaCombinada oferta = CrearOfertaNMasM(cantidadTotal: 4, unidadesRegaladas: 2);
            FakeOferta(oferta);
            // C y B pagadas a tarifa; 2×A (las dos más baratas del conjunto) gratis.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 1, 15m),
                Linea(PROD_B, 1, 12m),
                Linea(PROD_A, 2, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void UnidadesRegaladas_3Mas2_EsValido()
        {
            OfertaCombinada oferta = CrearOfertaNMasM(cantidadTotal: 5, unidadesRegaladas: 2);
            FakeOferta(oferta);
            // 3 pagadas a tarifa (2×C + B) y 2×A gratis.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 2, 15m),
                Linea(PROD_B, 1, 12m),
                Linea(PROD_A, 2, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void UnidadesRegaladas_2Mas2_UnaPagadaTresGratis_SeRechaza()
        {
            OfertaCombinada oferta = CrearOfertaNMasM(cantidadTotal: 4, unidadesRegaladas: 2);
            FakeOferta(oferta);
            // Solo se regalan 2 por instancia: pagar 1 y llevarse 3 gratis no cubre el suelo.
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 1, 15m),
                Linea(PROD_A, 2, 0m),
                Linea(PROD_B, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Con 2+2 solo salen gratis 2 unidades por oferta");
            StringAssert.Contains(respuesta.Motivo, "tarifa");
        }

        [TestMethod]
        public void UnidadesRegaladas_2Mas2_DosInstancias_CuatroGratis_EsValido()
        {
            OfertaCombinada oferta = CrearOfertaNMasM(cantidadTotal: 4, unidadesRegaladas: 2);
            FakeOferta(oferta);
            // 2×(2+2): 4×C pagadas a tarifa y 4×A gratis (las más baratas del conjunto).
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_C, 4, 15m),
                Linea(PROD_A, 4, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_A, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, respuesta.Motivo);
        }

        [TestMethod]
        public void UnidadesRegaladas_2Mas2_RegalarLaCara_SeRechazaIgualQueSiempre()
        {
            // La comprobación (a) — solo se regala lo más barato — no cambia con varias gratis.
            OfertaCombinada oferta = CrearOfertaNMasM(cantidadTotal: 4, unidadesRegaladas: 2);
            FakeOferta(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(PROD_A, 2, 10m),
                Linea(PROD_C, 1, 0m),
                Linea(PROD_B, 1, 0m));

            var respuesta = _validador.EsPedidoValido(pedido, PROD_C, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, "menor importe");
        }
    }
}
