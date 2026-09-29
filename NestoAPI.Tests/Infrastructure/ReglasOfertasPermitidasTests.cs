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
    /// NestoAPI#564 (Carlos, 29/09/26): Genéricos 6+1 salvo los desechables (subgrupo DES). La regla de denegación
    /// quita la autorización del mismo N+M (y sus múltiplos) a los productos a los que aplica; otra oferta
    /// autorizada (10+1) sigue valiendo. Antes «Denegar» no se miraba y hasta autorizaba (fila 720, producto 44731).
    /// </summary>
    [TestClass]
    public class ReglasOfertasPermitidasTests
    {
        private IServicioPrecios servicio;
        private IServicioPrecios servicioAnterior;

        private static OfertaPermitida Regla(int conPrecio, int regalo, bool denegar = false, string familia = "Genéricos", string numero = null, string subGrupo = null)
            => new OfertaPermitida { Empresa = "1", Familia = familia, Número = numero, CantidadConPrecio = (short)conPrecio, CantidadRegalo = (short)regalo, Denegar = denegar, SubGrupo = subGrupo };

        [TestInitialize]
        public void Setup()
        {
            servicioAnterior = GestorPrecios.servicio;
            servicio = A.Fake<IServicioPrecios>();
            GestorPrecios.servicio = servicio;
        }

        [TestCleanup]
        public void Cleanup()
        {
            GestorPrecios.servicio = servicioAnterior;
        }

        private Producto ConProducto(string numero, string subGrupo, params OfertaPermitida[] reglas)
        {
            var producto = new Producto { Empresa = "1", Número = numero, Nombre = "TOALLA DESECHABLE", PVP = 10, Familia = "Genéricos", Grupo = "PEL", SubGrupo = subGrupo };
            A.CallTo(() => servicio.BuscarProducto(numero)).Returns(producto);
            A.CallTo(() => servicio.BuscarOfertasPermitidas(numero)).Returns(new List<OfertaPermitida>(reglas));
            return producto;
        }

        private static PedidoVentaDTO Pedido(string producto, int cobradas, int regaladas)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "5", contacto = "0" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = producto, AplicarDescuento = true, Cantidad = (short)cobradas, PrecioUnitario = 10 });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = producto, AplicarDescuento = true, Cantidad = (short)regaladas, PrecioUnitario = 0 });
            return pedido;
        }

        private RespuestaValidacion Validar(Producto producto, int cobradas, int regaladas)
            => ValidadorOfertasPermitidas.EsOfertaPermitida(producto, Pedido(producto.Número, cobradas, regaladas), servicio);

        [TestMethod]
        public void Desechable_6mas1_Denegado()
        {
            var producto = ConProducto("40587", "DES", Regla(6, 1), Regla(6, 1, denegar: true, subGrupo: "DES"));

            RespuestaValidacion r = Validar(producto, 6, 1);

            Assert.IsFalse(r.ValidacionSuperada);
            Assert.IsTrue(r.AutorizadaDenegadaExpresamente, "Una denegación no la salta ningún validador de aceptación");
            StringAssert.Contains(r.Motivo, "denegada");
        }

        [TestMethod]
        public void Desechable_Multiplo12mas2_Denegado()
        {
            var producto = ConProducto("40587", "DES", Regla(6, 1), Regla(6, 1, denegar: true, subGrupo: "DES"));

            Assert.IsFalse(Validar(producto, 12, 2).ValidacionSuperada);
        }

        [TestMethod]
        public void Desechable_7mas1_SinOtraAutorizacion_NoVale()
        {
            // El 7+1 solo valía «por ser menos generoso» que el 6+1; anulado el 6+1, no queda nada que lo autorice
            var producto = ConProducto("40587", "DES", Regla(6, 1), Regla(6, 1, denegar: true, subGrupo: "DES"));

            Assert.IsFalse(Validar(producto, 7, 1).ValidacionSuperada);
        }

        [TestMethod]
        public void Desechable_10mas1Autorizado_SiVale()
        {
            var producto = ConProducto("40587", "DES", Regla(6, 1), Regla(6, 1, denegar: true, subGrupo: "DES"), Regla(10, 1));

            Assert.IsTrue(Validar(producto, 10, 1).ValidacionSuperada);
        }

        [TestMethod]
        public void GenericoNoDesechable_6mas1_Vale()
        {
            // La denegación con subgrupo DES no llega a los demás: la filtra BuscarOfertasPermitidas
            var producto = ConProducto("25000", "002", Regla(6, 1));

            Assert.IsTrue(Validar(producto, 6, 1).ValidacionSuperada);
        }

        [TestMethod]
        public void Fila720_DenegacionDeProducto_YaNoAutoriza()
        {
            // Producto 44731: fila específica «6+1 Denegar». Antes se tomaba como autorización expresa.
            var producto = ConProducto("44731", "002", Regla(6, 1), Regla(6, 1, denegar: true, familia: null, numero: "44731"));

            RespuestaValidacion r = Validar(producto, 6, 1);

            Assert.IsFalse(r.ValidacionSuperada);
            StringAssert.Contains(r.Motivo, "denegada");
        }

        [TestMethod]
        public void Reglas_MultiplosYProporcion()
        {
            Assert.IsTrue(ReglasOfertasPermitidas.EsMultiplo(Regla(6, 1), 6, 1));
            Assert.IsTrue(ReglasOfertasPermitidas.EsMultiplo(Regla(6, 1), 18, 3));
            Assert.IsFalse(ReglasOfertasPermitidas.EsMultiplo(Regla(6, 1), 7, 1));
            Assert.IsFalse(ReglasOfertasPermitidas.EsMultiplo(Regla(6, 1), 10, 1));
            Assert.IsTrue(ReglasOfertasPermitidas.MismaProporcion(Regla(6, 1), Regla(12, 2)));
            Assert.IsFalse(ReglasOfertasPermitidas.MismaProporcion(Regla(6, 1), Regla(10, 1)));
        }
    }
}
