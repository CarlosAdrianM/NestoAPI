using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Campanas;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    /// <summary>
    /// Una fila de CAMPAÑA del producto autoriza, pero no es un techo (Carlos 25/11/26, caso Israel: cliente 206, Lisap 43165 con
    /// la fila de Black Friday del 25 % y el 52 % pactado que antes autorizaba una fila de familia o grupo).
    /// </summary>
    [TestClass]
    public class ValidadorDescuentosPermitidosCampanaTests
    {
        private const string CAMPANA = "Black Friday 2026";
        private const string CLIENTE = "206";

        [TestCleanup]
        public void Limpiar()
        {
            CampanasAcumulanDistribuidores.Reiniciar();
            ClientesDistribuidores.Reiniciar();
        }

        private static Producto Lisap() => new Producto
        {
            Empresa = "1", Número = "43165", Nombre = "LISAP CHAMPU", PVP = 10M, Familia = "Lisap", Grupo = "PEL", SubGrupo = "ACB", Aplicar_Dto = true
        };

        private static DescuentosProducto Campana(decimal d) =>
            new DescuentosProducto { Empresa = "1", Nº_Producto = "43165", CantidadMínima = 1, Descuento = d, Campana = CAMPANA };

        private static DescuentosProducto PermanenteProducto(decimal d) =>
            new DescuentosProducto { Empresa = "1", Nº_Producto = "43165", CantidadMínima = 1, Descuento = d };

        private static DescuentosProducto Familia(decimal d) =>
            new DescuentosProducto { Empresa = "1", Nº_Cliente = CLIENTE, Familia = "Lisap", CantidadMínima = 0, Descuento = d };

        private static DescuentosProducto Grupo(decimal d) =>
            new DescuentosProducto { Empresa = "1", Nº_Cliente = CLIENTE, GrupoProducto = "PEL", CantidadMínima = 0, Descuento = d };

        private static RespuestaValidacion Validar(decimal descuento, params DescuentosProducto[] filas)
        {
            Producto producto = Lisap();
            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto(producto.Número)).Returns(producto);
            A.CallTo(() => servicio.BuscarOfertasPermitidas(A<string>._)).Returns(new List<OfertaPermitida>());
            A.CallTo(() => servicio.BuscarDescuentosPermitidos(producto.Número, CLIENTE, "0")).Returns(filas.ToList());
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, contacto = "0" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto.Número,
                AplicarDescuento = true,
                Cantidad = 1,
                PrecioUnitario = 10M,
                DescuentoProducto = descuento
            });
            return ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, servicio);
        }

        // ---------------------------------------------------------------- LA REGLA

        [TestMethod]
        public void Regla_SoloCampanaDeProducto_CuentanTodas()
        {
            var filas = new List<DescuentosProducto> { Campana(0.25M), Familia(0.60M) };

            Assert.AreEqual(2, ReglaDescuentosQuePrevalecen.Filtrar(filas, "43165", 1).Count());
        }

        [TestMethod]
        public void Regla_ConPermanenteDeProducto_SoloLasDelProducto()
        {
            var filas = new List<DescuentosProducto> { PermanenteProducto(0.10M), Campana(0.25M), Familia(0.60M) };

            List<DescuentosProducto> r = ReglaDescuentosQuePrevalecen.Filtrar(filas, "43165 ", 1).ToList();

            Assert.AreEqual(2, r.Count);
            Assert.IsFalse(r.Any(d => d.Familia != null));
        }

        [TestMethod]
        public void Regla_PermanenteQueNoLlegaPorCantidad_NoTapa()
        {
            DescuentosProducto permanente = PermanenteProducto(0.10M);
            permanente.CantidadMínima = 6;
            var filas = new List<DescuentosProducto> { permanente, Familia(0.60M) };

            Assert.AreEqual(2, ReglaDescuentosQuePrevalecen.Filtrar(filas, "43165", 1).Count());
        }

        // ---------------------------------------------------------------- el redondeo a céntimos no inventa descuento

        private static RespuestaValidacion ValidarPrecio(decimal pvp, decimal precio, short cantidad, params DescuentosProducto[] filas)
        {
            Producto producto = Lisap();
            producto.PVP = pvp;
            IServicioPrecios servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto(producto.Número)).Returns(producto);
            A.CallTo(() => servicio.BuscarOfertasPermitidas(A<string>._)).Returns(new List<OfertaPermitida>());
            A.CallTo(() => servicio.BuscarDescuentosPermitidos(producto.Número, CLIENTE, "0")).Returns(filas.ToList());
            var pedido = new PedidoVentaDTO { empresa = "1", cliente = CLIENTE, contacto = "0" };
            pedido.Lineas.Add(new LineaPedidoVentaDTO
            {
                tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
                Producto = producto.Número,
                Cantidad = cantidad,
                PrecioUnitario = precio
            });
            return ValidadorDescuentosPermitidos.EsDescuentoPermitido(producto, pedido, servicio);
        }

        [TestMethod]
        public void CasoLaura_22450_PrecioDeLaTiendaConEl10JustoDeLaCampana_EsValido()
        {
            // 1,67 € con el 10 % = 1,503 €; redondeado a 1,50 «salía» un 10,18 % y la campaña del 10 % no lo autorizaba.
            RespuestaValidacion r = ValidarPrecio(1.67M, 1.5030M, 8, Campana(0.10M));

            Assert.IsTrue(r.ValidacionSuperada, r.Motivo);
        }

        [TestMethod]
        public void PrecioPorDebajoDelDeLaCampana_SigueSinSerValido()
        {
            // 1,48 € es un 11,4 % de verdad: el 10 % no lo autoriza, con redondeo o sin él.
            RespuestaValidacion r = ValidarPrecio(1.67M, 1.48M, 8, Campana(0.10M));

            Assert.IsFalse(r.ValidacionSuperada);
        }

        [TestMethod]
        public void RedondeoQueFavorecia_SigueValiendo()
        {
            // 16,65 € con el 25 % = 12,4875 €; redondeado a 12,49 da un 24,98 %: valía antes y vale ahora.
            RespuestaValidacion r = ValidarPrecio(16.65M, 12.4875M, 1, Campana(0.25M));

            Assert.IsTrue(r.ValidacionSuperada, r.Motivo);
        }

        // ---------------------------------------------------------------- la validación

        [TestMethod]
        public void CasoIsrael_CampanaDeProducto25YFamilia60_El52EsValido()
        {
            RespuestaValidacion r = Validar(0.52M, Campana(0.25M), Familia(0.60M));

            Assert.IsTrue(r.ValidacionSuperada, r.Motivo);
        }

        [TestMethod]
        public void CasoIsrael_CampanaDeProducto25YGrupo60_El52EsValido()
        {
            RespuestaValidacion r = Validar(0.52M, Campana(0.25M), Grupo(0.60M));

            Assert.IsTrue(r.ValidacionSuperada, r.Motivo);
        }

        [TestMethod]
        public void SoloCampana_El52NoEsValidoYEl25Si()
        {
            Assert.IsFalse(Validar(0.52M, Campana(0.25M)).ValidacionSuperada);
            Assert.IsTrue(Validar(0.25M, Campana(0.25M)).ValidacionSuperada);
        }

        [TestMethod]
        public void PermanenteDeProducto10YFamilia60_El20NoEsValido_ElTechoSeConserva()
        {
            RespuestaValidacion r = Validar(0.20M, PermanenteProducto(0.10M), Familia(0.60M));

            Assert.IsFalse(r.ValidacionSuperada);
            Assert.AreEqual("43165", r.ProductoId);
        }

        [TestMethod]
        public void PermanenteMasCampanaMasFamilia_LaCampanaSubeElTecho()
        {
            DescuentosProducto[] filas = { PermanenteProducto(0.10M), Campana(0.25M), Familia(0.60M) };

            Assert.IsTrue(Validar(0.25M, filas).ValidacionSuperada);
            Assert.IsFalse(Validar(0.30M, filas).ValidacionSuperada);
        }

        [TestMethod]
        public void Distribuidor_ConCampanaMarcada_IgualQueAntes()
        {
            CampanasAcumulanDistribuidores.ConfigurarPara(new CampanaAcumulaDistribuidores
            {
                Campana = CAMPANA, FechaDesde = DateTime.Today.AddDays(-1), FechaHasta = DateTime.Today.AddDays(5)
            });
            ClientesDistribuidores.ConfigurarPara(CLIENTE);

            // 1 − (1 − 0,30) × (1 − 0,20) = 0,44
            Assert.IsTrue(Validar(0.44M, Familia(0.30M), Campana(0.20M)).ValidacionSuperada);
            Assert.IsFalse(Validar(0.45M, Familia(0.30M), Campana(0.20M)).ValidacionSuperada);
        }
    }
}
