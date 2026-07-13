using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Issue #289: filas de filtro de ofertas combinadas por Grupo y/o Subgrupo del producto,
    /// combinables en AND con familia/prefijo. Caso real: 2+1 de "Aceites, fluidos y geles
    /// profesionales" CV = UNA fila de filtro (Familia CV + Grupo COS + Subgrupo 107, cantidad 3,
    /// precio 0) + RegalarMenorImporte (#290), porque las referencias tienen tarifas distintas.
    /// </summary>
    [TestClass]
    public class ValidadorOfertasCombinadasGrupoSubgrupoTests
    {
        private const string ACEITE_CARO = "AC_CARO";     // tarifa 17
        private const string ACEITE_MEDIO = "AC_MEDIO";   // tarifa 15
        private const string ACEITE_BARATO = "AC_BARATO"; // tarifa 12
        private const string FAMILIA = "CV";
        private const string GRUPO = "COS";
        private const string SUBGRUPO = "107";
        private static readonly string[] ACEITES = { ACEITE_CARO, ACEITE_MEDIO, ACEITE_BARATO };

        private IServicioPrecios _servicio;
        private ValidadorOfertasCombinadas _validador;

        [TestInitialize]
        public void Setup()
        {
            _servicio = A.Fake<IServicioPrecios>();
            _validador = new ValidadorOfertasCombinadas();
            A.CallTo(() => _servicio.BuscarProducto(ACEITE_CARO)).Returns(new Producto { Número = ACEITE_CARO, PVP = 17m });
            A.CallTo(() => _servicio.BuscarProducto(ACEITE_MEDIO)).Returns(new Producto { Número = ACEITE_MEDIO, PVP = 15m });
            A.CallTo(() => _servicio.BuscarProducto(ACEITE_BARATO)).Returns(new Producto { Número = ACEITE_BARATO, PVP = 12m });
        }

        private OfertaCombinada CrearOferta2Mas1PorSubgrupo(string familia = FAMILIA)
        {
            return new OfertaCombinada
            {
                Id = 400,
                Empresa = "1",
                ImporteMinimo = 0,
                RegalarMenorImporte = true,
                OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
                {
                    new OfertaCombinadaDetalle
                    {
                        OfertaId = 400, Empresa = "1", Producto = null,
                        Familia = familia, Grupo = GRUPO, Subgrupo = SUBGRUPO,
                        Cantidad = 3, Precio = 0
                    }
                }
            };
        }

        // El matching real (nombre/familia/grupo/subgrupo) lo hace FiltrarLineas contra la BD; aquí
        // lo simulamos: SOLO la sobrecarga con grupo y subgrupo correctos devuelve los aceites. Si
        // el validador llamase a la sobrecarga corta (sin grupo), el fake devolvería lista vacía y
        // el test fallaría: eso prueba que la fila con grupo/subgrupo usa el matching nuevo.
        private void FakeMatching(OfertaCombinada oferta, string familia = FAMILIA)
        {
            foreach (string producto in ACEITES)
            {
                A.CallTo(() => _servicio.BuscarOfertasCombinadas(producto)).Returns(new List<OfertaCombinada> { oferta });
            }
            A.CallTo(() => _servicio.FiltrarLineas(A<PedidoVentaDTO>._, string.Empty, familia, GRUPO, SUBGRUPO))
                .ReturnsLazily((PedidoVentaDTO p, string filtro, string fam, string grupo, string subgrupo) =>
                    p.Lineas.Where(l => ACEITES.Contains(l.Producto)).ToList());
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
        public void FiltroPorGrupoYSubgrupo_PaganLasDosCarasYRegalanLaBarata_EsValido()
        {
            OfertaCombinada oferta = CrearOferta2Mas1PorSubgrupo();
            FakeMatching(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(ACEITE_CARO, 1, 17m),
                Linea(ACEITE_MEDIO, 1, 15m),
                Linea(ACEITE_BARATO, 1, 0m));

            RespuestaValidacion respuesta = _validador.EsPedidoValido(pedido, ACEITE_BARATO, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, "El 2+1 del subgrupo con la barata gratis debe autorizarse. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void FiltroPorGrupoYSubgrupo_RegalanLaCara_SeRechaza()
        {
            OfertaCombinada oferta = CrearOferta2Mas1PorSubgrupo();
            FakeMatching(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(ACEITE_BARATO, 1, 12m),
                Linea(ACEITE_MEDIO, 1, 15m),
                Linea(ACEITE_CARO, 1, 0m));

            RespuestaValidacion respuesta = _validador.EsPedidoValido(pedido, ACEITE_CARO, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Regalar la referencia más cara del subgrupo debe rechazarse");
            StringAssert.Contains(respuesta.Motivo, "menor importe");
        }

        [TestMethod]
        public void FiltroPorGrupoYSubgrupoSinFamilia_TambienCasa()
        {
            // El subgrupo puede ir solo (sin familia ni prefijo): criterios en blanco no filtran.
            OfertaCombinada oferta = CrearOferta2Mas1PorSubgrupo(familia: null);
            FakeMatching(oferta, familia: null);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(ACEITE_CARO, 1, 17m),
                Linea(ACEITE_MEDIO, 1, 15m),
                Linea(ACEITE_BARATO, 1, 0m));

            RespuestaValidacion respuesta = _validador.EsPedidoValido(pedido, ACEITE_BARATO, _servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada, "El filtro solo por grupo/subgrupo debe casar. Motivo: " + respuesta.Motivo);
        }

        [TestMethod]
        public void FiltroPorGrupoYSubgrupo_UnidadesDeMasEnElFiltro_SeRechazanComoSobresurtido()
        {
            // 4 unidades gratis+pagadas cuando la oferta cubre 3: la gratis extra no la cubre la oferta.
            OfertaCombinada oferta = CrearOferta2Mas1PorSubgrupo();
            FakeMatching(oferta);
            PedidoVentaDTO pedido = CrearPedido(
                Linea(ACEITE_CARO, 1, 17m),
                Linea(ACEITE_MEDIO, 1, 15m),
                Linea(ACEITE_BARATO, 2, 0m));

            RespuestaValidacion respuesta = _validador.EsPedidoValido(pedido, ACEITE_BARATO, _servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada, "Con 4 unidades en un filtro de 3 la sobrante no debe autorizarla esta oferta");
        }

        // ==== Matching de la fila de filtro contra un producto (BuscarOfertasCombinadas) ====

        [TestMethod]
        public void DetalleFiltroCasaConProducto_GrupoYSubgrupoCoinciden_Casa()
        {
            var detalle = new OfertaCombinadaDetalle { Producto = null, Grupo = GRUPO, Subgrupo = SUBGRUPO };
            var producto = new Producto { Número = ACEITE_CARO, Grupo = "COS", SubGrupo = "107" };

            Assert.IsTrue(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_GrupoDistinto_NoCasa()
        {
            var detalle = new OfertaCombinadaDetalle { Producto = null, Grupo = GRUPO, Subgrupo = SUBGRUPO };
            var producto = new Producto { Número = ACEITE_CARO, Grupo = "PEL", SubGrupo = "107" };

            Assert.IsFalse(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_SubgrupoDistinto_NoCasa()
        {
            var detalle = new OfertaCombinadaDetalle { Producto = null, Grupo = GRUPO, Subgrupo = SUBGRUPO };
            var producto = new Producto { Número = ACEITE_CARO, Grupo = "COS", SubGrupo = "108" };

            Assert.IsFalse(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_SinGrupoNiSubgrupo_SigueCasandoSoloPorFamilia()
        {
            // Regresión #282: una fila sin grupo/subgrupo se comporta exactamente igual que antes.
            var detalle = new OfertaCombinadaDetalle { Producto = null, Familia = "Lisap" };
            var producto = new Producto { Número = ACEITE_CARO, Familia = "Lisap", Grupo = "COS", SubGrupo = "107" };

            Assert.IsTrue(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }

        [TestMethod]
        public void DetalleFiltroCasaConProducto_GrupoConPaddingDeChar3_Casa()
        {
            // Las columnas son char(3): en BD pueden venir con padding y el trim debe igualarlas.
            var detalle = new OfertaCombinadaDetalle { Producto = null, Grupo = "CV ", Subgrupo = "107" };
            var producto = new Producto { Número = ACEITE_CARO, Grupo = "CV", SubGrupo = "107 " };

            Assert.IsTrue(ServicioPrecios.DetalleFiltroCasaConProducto(detalle, producto));
        }
    }
}
