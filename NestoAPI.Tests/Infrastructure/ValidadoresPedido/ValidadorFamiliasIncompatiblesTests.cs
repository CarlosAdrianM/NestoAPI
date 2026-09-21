using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infrastructure.ValidadoresPedido
{
    /// <summary>
    /// NestoAPI#501: Kinetics no se vende a quien haya comprado Faby o Greenik en los últimos
    /// 24 meses. La pareja y la ventana viven en la tabla, así que estos tests las inyectan.
    /// </summary>
    [TestClass]
    public class ValidadorFamiliasIncompatiblesTests
    {
        private const string KINETICS = "Kinetics";
        private const string FABY = "Faby";
        private const string GREENIK = "Greenik";
        private const string PRODUCTO_KINETICS = "12345";
        private const string PRODUCTO_NORMAL = "67890";
        private const string CLIENTE = "15296";

        private IServicioPrecios servicio;
        private ValidadorFamiliasIncompatibles validador;

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioPrecios>();
            validador = new ValidadorFamiliasIncompatibles();

            A.CallTo(() => servicio.BuscarProducto(PRODUCTO_KINETICS))
                .Returns(new Producto { Número = PRODUCTO_KINETICS, Familia = KINETICS });
            A.CallTo(() => servicio.BuscarProducto(PRODUCTO_NORMAL))
                .Returns(new Producto { Número = PRODUCTO_NORMAL, Familia = "Otra" });

            // Por defecto ninguna familia está condicionada y el cliente no ha comprado nada.
            A.CallTo(() => servicio.BuscarIncompatibilidadesFamilia(A<string>._))
                .Returns(new List<FamiliaIncompatibilidad>());
            A.CallTo(() => servicio.UltimaCompraDeFamilia(A<string>._, A<string>._, A<int>._))
                .Returns(null);
        }

        private static PedidoVentaDTO PedidoCon(params string[] productos)
        {
            return new PedidoVentaDTO
            {
                cliente = CLIENTE,
                Lineas = productos.Select(p => new LineaPedidoVentaDTO
                {
                    Producto = p,
                    Cantidad = 1,
                    tipoLinea = (byte)Constantes.TiposLineaVenta.PRODUCTO
                }).ToList()
            };
        }

        private void KineticsCondicionadaA(string familiaIncompatible, int meses = 24)
        {
            A.CallTo(() => servicio.BuscarIncompatibilidadesFamilia(KINETICS))
                .Returns(new List<FamiliaIncompatibilidad>
                {
                    new FamiliaIncompatibilidad
                    {
                        Empresa = "1",
                        Familia = KINETICS,
                        FamiliaIncompatible = familiaIncompatible,
                        Meses = meses,
                        Estado = 0
                    }
                });
        }

        [TestMethod]
        public void SiElClienteComproLaFamiliaIncompatible_NoDejaVender()
        {
            KineticsCondicionadaA(FABY);
            A.CallTo(() => servicio.UltimaCompraDeFamilia(CLIENTE, FABY, 24))
                .Returns(new DateTime(2026, 3, 17));

            RespuestaValidacion respuesta = validador.EsPedidoValido(PedidoCon(PRODUCTO_KINETICS), servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, KINETICS);
            StringAssert.Contains(respuesta.Motivo, FABY);
            StringAssert.Contains(respuesta.Motivo, "17/03/2026");
            Assert.AreEqual(PRODUCTO_KINETICS, respuesta.ProductoId);
            Assert.AreEqual(PRODUCTO_KINETICS, respuesta.Errores.Single().ProductoId);
        }

        [TestMethod]
        public void SiElClienteNoHaCompradoLaFamiliaIncompatible_DejaVender()
        {
            KineticsCondicionadaA(FABY);

            RespuestaValidacion respuesta = validador.EsPedidoValido(PedidoCon(PRODUCTO_KINETICS), servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
        }

        [TestMethod]
        public void MiraTodasLasFamiliasIncompatibles_NoSoloLaPrimera()
        {
            A.CallTo(() => servicio.BuscarIncompatibilidadesFamilia(KINETICS))
                .Returns(new List<FamiliaIncompatibilidad>
                {
                    new FamiliaIncompatibilidad { Familia = KINETICS, FamiliaIncompatible = FABY, Meses = 24 },
                    new FamiliaIncompatibilidad { Familia = KINETICS, FamiliaIncompatible = GREENIK, Meses = 24 }
                });
            A.CallTo(() => servicio.UltimaCompraDeFamilia(CLIENTE, GREENIK, 24))
                .Returns(new DateTime(2025, 11, 4));

            RespuestaValidacion respuesta = validador.EsPedidoValido(PedidoCon(PRODUCTO_KINETICS), servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            StringAssert.Contains(respuesta.Motivo, GREENIK);
        }

        [TestMethod]
        public void UnPedidoSinFamiliasCondicionadas_NiSiquieraPreguntaPorLasCompras()
        {
            RespuestaValidacion respuesta = validador.EsPedidoValido(PedidoCon(PRODUCTO_NORMAL), servicio);

            Assert.IsTrue(respuesta.ValidacionSuperada);
            A.CallTo(() => servicio.UltimaCompraDeFamilia(A<string>._, A<string>._, A<int>._))
                .MustNotHaveHappened();
        }

        [TestMethod]
        public void LaFamiliaDeCadaProductoSeConsultaUnaSolaVez_AunqueElPedidoTraigaVariasLineas()
        {
            KineticsCondicionadaA(FABY);

            _ = validador.EsPedidoValido(PedidoCon(PRODUCTO_KINETICS, PRODUCTO_KINETICS, PRODUCTO_KINETICS), servicio);

            A.CallTo(() => servicio.BuscarProducto(PRODUCTO_KINETICS)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void SiLaFamiliaCondicionadaVaConOtrosProductos_ElErrorSenalaSoloLosSuyos()
        {
            KineticsCondicionadaA(FABY);
            A.CallTo(() => servicio.UltimaCompraDeFamilia(CLIENTE, FABY, 24))
                .Returns(DateTime.Today);

            RespuestaValidacion respuesta = validador.EsPedidoValido(
                PedidoCon(PRODUCTO_NORMAL, PRODUCTO_KINETICS), servicio);

            Assert.IsFalse(respuesta.ValidacionSuperada);
            Assert.AreEqual(1, respuesta.Errores.Count);
            Assert.AreEqual(PRODUCTO_KINETICS, respuesta.Errores.Single().ProductoId);
        }

        [TestMethod]
        public void UnPedidoVacioOSinCliente_NoRompe()
        {
            Assert.IsTrue(validador.EsPedidoValido(new PedidoVentaDTO
            {
                cliente = CLIENTE,
                Lineas = new List<LineaPedidoVentaDTO>()
            }, servicio).ValidacionSuperada);

            Assert.IsTrue(validador.EsPedidoValido(new PedidoVentaDTO
            {
                cliente = null,
                Lineas = new List<LineaPedidoVentaDTO> { new LineaPedidoVentaDTO { Producto = PRODUCTO_KINETICS, Cantidad = 1 } }
            }, servicio).ValidacionSuperada);
        }
    }
}
