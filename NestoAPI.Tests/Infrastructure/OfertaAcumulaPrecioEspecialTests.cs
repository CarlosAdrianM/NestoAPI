using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// Novedades 1263 (13/11/26), continuación de NestoAPI#589. La regla general no cambia: un N+M exige cobrar a precio
    /// de ficha (o precio especial o la oferta, nunca las dos). Excepción por DATO: una oferta permitida propia del
    /// producto marcada como «admite además el precio especial» (OfertasPermitidas.AcumulaPrecioEspecial). Caso real:
    /// los packs 45929, 45930 y 45931 llevan precio especial y 6+1 durante la feria online (autorizado por Dirección
    /// comercial). Aquí, el 45931: ficha 34,93 €, precio especial 28 €, oferta 843 (6+1, marcada).
    /// </summary>
    [TestClass]
    public class OfertaAcumulaPrecioEspecialTests
    {
        private const string PACK = "45931";
        private const int OFERTA_PACK = 843;
        private const int OFERTA_FAMILIA = 41;

        private IServicioPrecios servicio;
        private IServicioPrecios servicioAnterior;
        private Func<HashSet<int>> lectorAnterior;
        private Producto producto;

        [TestInitialize]
        public void Setup()
        {
            servicioAnterior = GestorPrecios.servicio;
            servicio = A.Fake<IServicioPrecios>();
            GestorPrecios.servicio = servicio;
            lectorAnterior = OfertasQueAcumulanPrecioEspecial.Lector;
            Marcar(OFERTA_PACK);

            producto = new Producto { Empresa = "1", Número = PACK, Nombre = "PACK 26 COFRE 24 H", PVP = 34.93M, Familia = "CV", Grupo = "COS", SubGrupo = "PRG" };
            A.CallTo(() => servicio.BuscarProducto(PACK)).Returns(producto);
            A.CallTo(() => servicio.BuscarOfertasPermitidas(PACK)).Returns(new List<OfertaPermitida> { OfertaFamilia(), OfertaPack() });
            A.CallTo(() => servicio.BuscarDescuentosPermitidos(PACK, A<string>._, A<string>._)).Returns(new List<DescuentosProducto> { PrecioEspecial(28M) });
        }

        [TestCleanup]
        public void Cleanup()
        {
            GestorPrecios.servicio = servicioAnterior;
            OfertasQueAcumulanPrecioEspecial.Lector = lectorAnterior;
            OfertasQueAcumulanPrecioEspecial.Invalidar();
        }

        private static void Marcar(params int[] ofertas)
        {
            OfertasQueAcumulanPrecioEspecial.Lector = () => new HashSet<int>(ofertas);
            OfertasQueAcumulanPrecioEspecial.Invalidar();
        }

        private static OfertaPermitida OfertaFamilia()
            => new OfertaPermitida { Empresa = "1", NºOrden = OFERTA_FAMILIA, Familia = "CV", CantidadConPrecio = 6, CantidadRegalo = 1 };

        private static OfertaPermitida OfertaPack(int conPrecio = 6, int regalo = 1)
            => new OfertaPermitida { Empresa = "1", NºOrden = OFERTA_PACK, Número = PACK + "          ", CantidadConPrecio = (short)conPrecio, CantidadRegalo = (short)regalo };

        private static DescuentosProducto PrecioEspecial(decimal precio, short cantidadMinima = 0, string numero = PACK)
            => new DescuentosProducto { Empresa = "1", Nº_Producto = numero, Precio = precio, CantidadMínima = cantidadMinima };

        private static PedidoVentaDTO Pedido(int cobradas, decimal precio, int regaladas, decimal descuento = 0)
        {
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = "5", contacto = "0" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PACK, AplicarDescuento = false, Cantidad = (short)cobradas, PrecioUnitario = precio, DescuentoLinea = descuento });
            pedido.Lineas.Add(new LineaPedidoVentaDTO { tipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PACK, AplicarDescuento = false, Cantidad = (short)regaladas, PrecioUnitario = 0 });
            return pedido;
        }

        private RespuestaValidacion Validar(int cobradas, decimal precio, int regaladas, decimal descuento = 0)
            => ValidadorOfertasPermitidas.EsOfertaPermitida(producto, Pedido(cobradas, precio, regaladas, descuento), servicio);

        // ------------------------------------------------------------- La regla pura: qué precio especial se admite

        private decimal? Admitido(int cobradas, int regaladas, List<OfertaPermitida> ofertas = null, List<DescuentosProducto> precios = null)
            => ReglasOfertasPermitidas.PrecioEspecialAdmitido(ofertas ?? new List<OfertaPermitida> { OfertaFamilia(), OfertaPack() }, producto,
                cobradas, regaladas, precios ?? new List<DescuentosProducto> { PrecioEspecial(28M) }, OfertasQueAcumulanPrecioEspecial.LaAcumula);

        [TestMethod]
        public void PrecioEspecialAdmitido_OfertaPropiaMarcada_ElPrecioEspecial()
        {
            Assert.AreEqual(28M, Admitido(6, 1));
            Assert.AreEqual(28M, Admitido(12, 2), "Un múltiplo también");
            Assert.AreEqual(28M, Admitido(7, 1), "Más unidades de pago de las que pide la oferta también");
        }

        [TestMethod]
        public void PrecioEspecialAdmitido_SinMarca_Ninguno()
        {
            Marcar();

            Assert.IsNull(Admitido(6, 1));
        }

        [TestMethod]
        public void PrecioEspecialAdmitido_SoloLaOfertaDeLaFamiliaMarcada_Ninguno()
        {
            // La marca tiene que estar en una oferta PROPIA del producto: marcar el 6+1 de toda la familia no vale
            Marcar(OFERTA_FAMILIA);

            Assert.IsNull(Admitido(6, 1));
        }

        [TestMethod]
        public void PrecioEspecialAdmitido_LaOfertaDelPedidoNoCabeEnLaMarcada_Ninguno()
        {
            Assert.IsNull(Admitido(3, 1), "Un 3+1 es más generoso que el 6+1 autorizado");
            Assert.IsNull(Admitido(6, 0), "Sin unidades de regalo no hay oferta");
        }

        [TestMethod]
        public void PrecioEspecialAdmitido_OfertaDenegadaODeOtroProducto_Ninguno()
        {
            OfertaPermitida denegada = OfertaPack();
            denegada.Denegar = true;
            OfertaPermitida deOtro = OfertaPack();
            deOtro.Número = "45930";

            Assert.IsNull(Admitido(6, 1, new List<OfertaPermitida> { denegada }));
            Assert.IsNull(Admitido(6, 1, new List<OfertaPermitida> { deOtro }));
        }

        [TestMethod]
        public void PrecioEspecialAdmitido_SinPrecioEspecialDelProducto_Ninguno()
        {
            var soloDescuentoDeFamilia = new List<DescuentosProducto> { new DescuentosProducto { Empresa = "1", Familia = "CV", Descuento = 0.15M } };
            var deOtroProducto = new List<DescuentosProducto> { PrecioEspecial(20M, numero: "45930") };
            var sinAlcanzarLaCantidad = new List<DescuentosProducto> { PrecioEspecial(25M, cantidadMinima: 12) };

            Assert.IsNull(Admitido(6, 1, precios: soloDescuentoDeFamilia));
            Assert.IsNull(Admitido(6, 1, precios: deOtroProducto));
            Assert.IsNull(Admitido(6, 1, precios: sinAlcanzarLaCantidad));
        }

        [TestMethod]
        public void PrecioEspecialAdmitido_VariosPrecios_ElMasBajoQueAlcanzaLaCantidad()
        {
            var precios = new List<DescuentosProducto> { PrecioEspecial(28M), PrecioEspecial(26M, cantidadMinima: 6), PrecioEspecial(24M, cantidadMinima: 24) };

            Assert.AreEqual(26M, Admitido(6, 1, precios: precios));
        }

        // ------------------------------------------------------------- El motivo de rechazo

        [TestMethod]
        public void Motivo_ConPrecioEspecialAdmitido_ElSueloEsElPrecioEspecial()
        {
            var alEspecial = new PrecioDescuentoProducto { producto = producto, cantidad = 6, cantidadOferta = 1, precioCalculado = 28M };
            var porDebajo = new PrecioDescuentoProducto { producto = producto, cantidad = 6, cantidadOferta = 1, precioCalculado = 27.99M };
            var conDescuento = new PrecioDescuentoProducto { producto = producto, cantidad = 6, cantidadOferta = 1, precioCalculado = 28M, descuentoCalculado = 0.1M };

            Assert.IsNull(ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa(alEspecial, producto, 28M));
            StringAssert.Contains(ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa(porDebajo, producto, 28M), "precio especial");
            StringAssert.Contains(ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa(conDescuento, producto, 28M), "no puede llevar descuento");
        }

        [TestMethod]
        public void Motivo_SinPrecioEspecialAdmitido_LaReglaDeSiempre()
        {
            var alEspecial = new PrecioDescuentoProducto { producto = producto, cantidad = 6, cantidadOferta = 1, precioCalculado = 28M };
            var aFicha = new PrecioDescuentoProducto { producto = producto, cantidad = 6, cantidadOferta = 1, precioCalculado = 34.93M };

            StringAssert.Contains(ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa(alEspecial, producto), "inferior al de ficha");
            StringAssert.Contains(ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa(alEspecial, producto, null), "inferior al de ficha");
            Assert.IsNull(ReglasOfertasPermitidas.MotivoCobradoFueraDeTarifa(aFicha, producto));
        }

        // ------------------------------------------------------------- La validación del pedido, de punta a punta

        [TestMethod]
        public void Pedido_6mas1AlPrecioEspecial_ConLaOfertaMarcada_Vale()
        {
            RespuestaValidacion r = Validar(6, 28M, 1);

            Assert.IsTrue(r.ValidacionSuperada, r.Motivo);
        }

        [TestMethod]
        public void Pedido_6mas1AlPrecioEspecial_SinMarca_SeRechazaComoSiempre()
        {
            Marcar();

            RespuestaValidacion r = Validar(6, 28M, 1);

            Assert.IsFalse(r.ValidacionSuperada);
            StringAssert.Contains(r.Motivo, "inferior al de ficha");
        }

        [TestMethod]
        public void Pedido_6mas1PorDebajoDelPrecioEspecial_SeRechaza()
        {
            RespuestaValidacion r = Validar(6, 25M, 1);

            Assert.IsFalse(r.ValidacionSuperada);
            StringAssert.Contains(r.Motivo, "precio especial");
        }

        [TestMethod]
        public void Pedido_6mas1APrecioDeFicha_SigueValiendo()
        {
            Assert.IsTrue(Validar(6, 34.93M, 1).ValidacionSuperada);
        }

        // ------------------------------------------------------------- El precio que se devuelve a la plantilla

        private PrecioDescuentoProducto PrecioDeFicha(bool aplicarDescuento = false)
            => new PrecioDescuentoProducto { producto = producto, cliente = "5", contacto = "0", cantidad = 6, precioCalculado = 34.93M, aplicarDescuento = aplicarDescuento };

        [TestMethod]
        public void CargarPrecio_ConUnidadesDeOfertaYLaOfertaMarcada_DevuelveElPrecioEspecialSinDescuento()
        {
            PrecioDescuentoProducto precio = PrecioDeFicha();

            PlantillaVentasController.AplicarPrecioEspecialQueAcumulaConLaOferta(precio, producto, "5", "0", 6, 1);

            Assert.AreEqual(28M, precio.precioCalculado);
            Assert.AreEqual(0M, precio.descuentoCalculado);
            Assert.IsFalse(precio.aplicarDescuento, "La línea sigue sin «aplicar descuento», como cualquier línea con oferta");
        }

        [TestMethod]
        public void CargarPrecio_SinUnidadesDeOferta_OSinMarca_OConDescuentoAplicado_NoTocaNada()
        {
            PrecioDescuentoProducto sinOferta = PrecioDeFicha();
            PlantillaVentasController.AplicarPrecioEspecialQueAcumulaConLaOferta(sinOferta, producto, "5", "0", 6, 0);
            Assert.AreEqual(34.93M, sinOferta.precioCalculado, "Un cliente anterior no manda las unidades de oferta");

            PrecioDescuentoProducto conDescuento = PrecioDeFicha(aplicarDescuento: true);
            PlantillaVentasController.AplicarPrecioEspecialQueAcumulaConLaOferta(conDescuento, producto, "5", "0", 6, 1);
            Assert.AreEqual(34.93M, conDescuento.precioCalculado, "Con «aplicar descuento» el precio ya lo ha puesto el motor de precios");

            Marcar();
            PrecioDescuentoProducto sinMarca = PrecioDeFicha();
            PlantillaVentasController.AplicarPrecioEspecialQueAcumulaConLaOferta(sinMarca, producto, "5", "0", 6, 1);
            Assert.AreEqual(34.93M, sinMarca.precioCalculado);
        }

        [TestMethod]
        public void CargarPrecio_SiFallaLaLectura_QuedaElPrecioDeFichaYNoLanza()
        {
            A.CallTo(() => servicio.BuscarOfertasPermitidas(PACK)).Throws(new InvalidOperationException("sin base de datos"));
            PrecioDescuentoProducto precio = PrecioDeFicha();

            PlantillaVentasController.AplicarPrecioEspecialQueAcumulaConLaOferta(precio, producto, "5", "0", 6, 1);

            Assert.AreEqual(34.93M, precio.precioCalculado);
        }

        // ------------------------------------------------------------- La lectura de la marca

        [TestMethod]
        public void Marca_SiLaLecturaFalla_NingunaOfertaAcumulaYNoLanza()
        {
            OfertasQueAcumulanPrecioEspecial.Lector = () => throw new InvalidOperationException("sin base de datos");
            OfertasQueAcumulanPrecioEspecial.Invalidar();

            Assert.IsFalse(OfertasQueAcumulanPrecioEspecial.LaAcumula(OfertaPack()));
            Assert.IsFalse(OfertasQueAcumulanPrecioEspecial.LaAcumula(null));
        }
    }
}
