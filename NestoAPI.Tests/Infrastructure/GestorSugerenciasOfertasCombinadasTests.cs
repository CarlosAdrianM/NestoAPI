using System.Collections.Generic;
using System.Linq;
using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.ValidadoresPedido;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#626: sugerencias de ofertas combinadas. Caso real: la 267 «Oferta mascarillas PDRN»
    /// (oct-2026), con dos filas a precio 0 (7 de la 45664 y 1 de la 45665) e importe mínimo de 132 €
    /// (6 × 22 €). La 45664 tiene además su propio 6+1 (NºOrden 795) y la 45665 otro (796).
    /// Los tests «circuito completo» validan con <see cref="GestorPrecios.EsPedidoValido"/> de verdad.
    /// </summary>
    [TestClass]
    public class GestorSugerenciasOfertasCombinadasTests
    {
        private const string MASCARILLA = "45664";
        private const string LIMPIADOR = "45665";
        private IServicioPrecios servicio;
        private static readonly System.Func<PedidoVentaDTO, RespuestaValidacion> Acepta = p => new RespuestaValidacion { ValidacionSuperada = true };
        private static readonly System.Func<PedidoVentaDTO, RespuestaValidacion> Deniega = p => new RespuestaValidacion { ValidacionSuperada = false, Motivo = "No" };

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto(MASCARILLA)).Returns(new Producto
            {
                Empresa = "1", Número = MASCARILLA, Nombre = "MASCARILLA PDRN", Familia = "Genéricos", Grupo = "COS", SubGrupo = "111", PVP = 22, Aplicar_Dto = true, Estado = 0
            });
            A.CallTo(() => servicio.BuscarProducto(LIMPIADOR)).Returns(new Producto
            {
                Empresa = "1", Número = LIMPIADOR, Nombre = "LIMPIADOR MOUSE PDRN", Familia = "Genéricos", Grupo = "COS", SubGrupo = "002", PVP = 13.8M, Aplicar_Dto = true, Estado = 0
            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas(MASCARILLA)).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { Empresa = "1", NºOrden = 795, Número = MASCARILLA, CantidadConPrecio = 6, CantidadRegalo = 1 }
            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas(LIMPIADOR)).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { Empresa = "1", NºOrden = 796, Número = LIMPIADOR, CantidadConPrecio = 6, CantidadRegalo = 1 }
            });
            OfertaCombinada o267 = Oferta267();
            A.CallTo(() => servicio.BuscarOfertasCombinadas(MASCARILLA)).Returns(new List<OfertaCombinada> { o267 });
            A.CallTo(() => servicio.BuscarOfertasCombinadas(LIMPIADOR)).Returns(new List<OfertaCombinada> { o267 });
            A.CallTo(() => servicio.BuscarStockDisponibleParaRegalar(A<string>._, A<string>._)).Returns(100);
        }

        // Tal cual está en producción (OfertasCombinadas 267 / OfertasCombinadasDetalle 1104-1105).
        private static OfertaCombinada Oferta267() => new OfertaCombinada
        {
            Id = 267,
            Empresa = "1  ",
            Nombre = "Oferta mascarillas PDRN",
            ImporteMinimo = 132,
            UnidadesRegaladas = 1,
            OfertasCombinadasDetalles = new List<OfertaCombinadaDetalle>
            {
                new OfertaCombinadaDetalle { Id = 1104, Empresa = "1  ", OfertaId = 267, Producto = MASCARILLA + "          ", Cantidad = 7, Precio = 0 },
                new OfertaCombinadaDetalle { Id = 1105, Empresa = "1  ", OfertaId = 267, Producto = LIMPIADOR + "          ", Cantidad = 1, Precio = 0 }
            }
        };

        // Cliente normal (no El Edén, que se salta la validación).
        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas) => new PedidoVentaDTO
        {
            empresa = "1",
            cliente = "10458",
            contacto = "0",
            Lineas = lineas.ToList()
        };

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, int id = 1, int? oferta = null, decimal descuentoLinea = 0) => new LineaPedidoVentaDTO
        {
            id = id,
            Producto = producto,
            Cantidad = cantidad,
            PrecioUnitario = precio,
            DescuentoLinea = descuentoLinea,
            oferta = oferta,
            tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            almacen = "ALG",
            GrupoProducto = "COS"
        };

        // Lo que mete hoy el comercial: 6 mascarillas cobradas + la del 6+1 (oferta 1 de la plantilla).
        private static PedidoVentaDTO PedidoPdrnConSu6Mas1() => Pedido(
            Linea(MASCARILLA, 6, 22, id: 1, oferta: 1),
            Linea(MASCARILLA, 1, 0, id: 2, oferta: 1));

        #region Caso 267, circuito de validación completo

        [TestMethod]
        public void Pdrn267_ConEl6Mas1Puesto_SugiereAnadirElLimpiadorDeRegalo()
        {
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(PedidoPdrnConSu6Mas1(), servicio);

            SugerenciaOfertaDTO regalo = s.Single();
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_REGALO_COMBINADA_NO_APLICADO, regalo.Tipo);
            Assert.AreEqual(GestorSugerenciasOfertas.ACCION_ANADIR_LINEA, regalo.Accion);
            Assert.AreEqual(LIMPIADOR, regalo.Producto);
            Assert.AreEqual(1, regalo.CantidadRegalo);
            Assert.AreEqual(0, regalo.CantidadActual);
            Assert.AreEqual(0, regalo.CantidadSugerida);
            Assert.AreEqual(1M, regalo.Descuento, "Al 100 %");
            Assert.AreEqual(267, regalo.OfertaCombinada);
            Assert.IsNull(regalo.Alternativas);
            Assert.AreEqual("El pedido cumple la oferta «Oferta mascarillas PDRN»: le corresponde 1 unidad del producto 45665 (LIMPIADOR MOUSE PDRN) de regalo y no se está aplicando.", regalo.Texto);
        }

        [TestMethod]
        public void Pdrn267_ConElLimpiadorComoLoMandaLaPlantilla_ElPedidoEsValidoYYaNoSeSugiere()
        {
            // Nesto#524 lo añade como unidades de oferta (precio 0 y su número de oferta), como el 6+1.
            PedidoVentaDTO pedido = PedidoPdrnConSu6Mas1();
            pedido.Lineas.Add(Linea(LIMPIADOR, 1, 0, id: 0, oferta: 2));

            RespuestaValidacion validacion = GestorPrecios.EsPedidoValido(pedido, servicio);

            Assert.IsTrue(validacion.ValidacionSuperada, validacion.Motivo);
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(pedido, servicio).Count);
        }

        [TestMethod]
        public void Pdrn267_ConElLimpiadorATarifaYCienPorCienDeDescuento_TambienEsValido()
        {
            // Otra forma de mandar el regalo (p. ej. NestoApp): precio de tarifa y 100 % de descuento, sin nº de oferta.
            PedidoVentaDTO pedido = PedidoPdrnConSu6Mas1();
            pedido.Lineas.Add(Linea(LIMPIADOR, 1, 13.8M, id: 0, descuentoLinea: 1));

            RespuestaValidacion validacion = GestorPrecios.EsPedidoValido(pedido, servicio);

            Assert.IsTrue(validacion.ValidacionSuperada, validacion.Motivo);
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(pedido, servicio).Count);
        }

        [TestMethod]
        public void Pdrn267_SeisMascarillasSinEl6Mas1_PrimeroSugiereEl6Mas1YNoLaCombinada()
        {
            // La unidad que le falta a la combinada es la gratis del 6+1: no se ofrecen las dos cosas a la vez.
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea(MASCARILLA, 6, 22)), servicio);

            SugerenciaOfertaDTO nMasM = s.Single();
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_OFERTA_NO_APLICADA, nMasM.Tipo);
            Assert.AreEqual(GestorSugerenciasOfertas.ACCION_MODIFICAR_LINEA, nMasM.Accion);
            Assert.AreEqual(MASCARILLA, nMasM.Producto);
        }

        [TestMethod]
        public void Pdrn267_ConDescuentoNoLlegaAlImporteMinimo_NoSugiereElRegalo()
        {
            // 6 × 22 € con un 10 % = 118,80 €, por debajo de los 132 € de la oferta.
            PedidoVentaDTO pedido = Pedido(
                Linea(MASCARILLA, 6, 22, id: 1, oferta: 1, descuentoLinea: 0.1M),
                Linea(MASCARILLA, 1, 0, id: 2, oferta: 1));

            Assert.IsFalse(GestorSugerenciasOfertas.Calcular(pedido, servicio).Any(x => x.Tipo == GestorSugerenciasOfertas.TIPO_REGALO_COMBINADA_NO_APLICADO));
        }

        [TestMethod]
        public void Pdrn267_SinStockDelLimpiador_NoSePromete()
        {
            A.CallTo(() => servicio.BuscarStockDisponibleParaRegalar(LIMPIADOR, A<string>._)).Returns(0);

            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(PedidoPdrnConSu6Mas1(), servicio).Count);
        }

        [TestMethod]
        public void Pdrn267_DosVecesLaOferta_SugiereDosLimpiadores()
        {
            // 14 mascarillas cobradas (308 €): dos veces la combinada. Sale también el 6+1 de la mascarilla.
            PedidoVentaDTO pedido = Pedido(Linea(MASCARILLA, 14, 22));

            SugerenciaOfertaDTO regalo = GestorSugerenciasOfertas.Calcular(pedido, servicio)
                .Single(x => x.Tipo == GestorSugerenciasOfertas.TIPO_REGALO_COMBINADA_NO_APLICADO);

            Assert.AreEqual(LIMPIADOR, regalo.Producto);
            Assert.AreEqual(2, regalo.CantidadRegalo);
            StringAssert.Contains(regalo.Texto, "le corresponden 2 unidades del producto 45665");
        }

        [TestMethod]
        public void Pdrn267_DosVecesConEl6Mas1EnLineaAparte_SoloSugiereLoQueAceptaElValidador()
        {
            // 12 cobradas + 2 del 6+1: el control de múltiplos de ValidadorOfertasCombinadas mira cada línea por
            // separado (12/7 < 2/1) y rechaza el 2º limpiador. Se sugiere lo que el pedido aceptaría: uno.
            PedidoVentaDTO pedido = Pedido(
                Linea(MASCARILLA, 12, 22, id: 1, oferta: 1),
                Linea(MASCARILLA, 2, 0, id: 2, oferta: 1));
            PedidoVentaDTO conDos = Pedido(pedido.Lineas.Concat(new[] { Linea(LIMPIADOR, 2, 0, id: 0, oferta: 2) }).ToArray());
            Assert.IsFalse(GestorPrecios.EsPedidoValido(conDos, servicio).ValidacionSuperada, "Así valida hoy el pedido");

            SugerenciaOfertaDTO regalo = GestorSugerenciasOfertas.Calcular(pedido, servicio).Single();

            Assert.AreEqual(1, regalo.CantidadRegalo);
        }

        [TestMethod]
        public void Pdrn267_ElLimpiadorYaVaCobrado_NoSeLeProponeRegalo()
        {
            // Si el limpiador ya se paga, es parte de lo cobrado: no se inventa una línea de regalo encima.
            PedidoVentaDTO pedido = PedidoPdrnConSu6Mas1();
            pedido.Lineas.Add(Linea(LIMPIADOR, 1, 13.8M, id: 3));

            Assert.IsFalse(GestorSugerenciasOfertas.Calcular(pedido, servicio).Any(x => x.OfertaCombinada.HasValue));
        }

        #endregion

        #region Reglas (validación inyectada)

        [TestMethod]
        public void SiElPedidoLoRechazaria_NoSeSugiere()
        {
            Assert.AreEqual(0, GestorSugerenciasOfertas.SugerirCombinadas(PedidoPdrnConSu6Mas1(), servicio, Deniega).Count);
        }

        [TestMethod]
        public void SiSoloLoAceptaElCincoPorCientoDeMuestras_NoEsLaOfertaYNoSeSugiere()
        {
            // El circuito da el pedido por bueno (como haría el 5 % de material promocional en un pedido grande),
            // pero ninguna combinada autoriza el regalo (aquí, porque para el limpiador no hay oferta vigente).
            A.CallTo(() => servicio.BuscarOfertasCombinadas(LIMPIADOR)).Returns(new List<OfertaCombinada>());

            Assert.AreEqual(0, GestorSugerenciasOfertas.SugerirCombinadas(PedidoPdrnConSu6Mas1(), servicio, Acepta).Count);
        }

        [TestMethod]
        public void ElPedidoQueSeValida_LlevaElRegaloComoLoMandaLaPlantilla()
        {
            PedidoVentaDTO validado = null;
            PedidoVentaDTO original = PedidoPdrnConSu6Mas1();

            GestorSugerenciasOfertas.SugerirCombinadas(original, servicio, p => { validado = p; return new RespuestaValidacion { ValidacionSuperada = true }; });

            LineaPedidoVentaDTO regalo = validado.Lineas.Last();
            Assert.AreEqual(LIMPIADOR, regalo.Producto);
            Assert.AreEqual(1, regalo.Cantidad);
            Assert.AreEqual(0, regalo.PrecioUnitario);
            Assert.AreEqual(0, regalo.BaseImponible);
            Assert.AreEqual(0, regalo.id, "Línea nueva");
            Assert.AreEqual(2, regalo.oferta, "Un nº de oferta nuevo, como la plantilla");
            Assert.AreEqual("ALG", regalo.almacen);
            Assert.AreEqual(2, original.Lineas.Count, "El pedido original no se toca");
        }

        private void OfertaTresDeAConB(params string[] regalos)
        {
            A.CallTo(() => servicio.BuscarProducto("A")).Returns(new Producto { Número = "A", Nombre = "CREMA A", PVP = 10, Aplicar_Dto = true });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("A")).Returns(new List<OfertaPermitida>());
            var detalles = new List<OfertaCombinadaDetalle> { new OfertaCombinadaDetalle { Producto = "A", Cantidad = 3, Precio = 10 } };
            foreach (string r in regalos)
            {
                detalles.Add(new OfertaCombinadaDetalle { Producto = r, Cantidad = 1, Precio = 0, GrupoAlternativa = regalos.Length > 1 ? 1 : (int?)null });
                A.CallTo(() => servicio.BuscarProducto(r)).Returns(new Producto { Número = r, Nombre = "REGALO " + r, PVP = 5 });
            }
            var oferta = new OfertaCombinada { Id = 300, Empresa = "1", Nombre = "Tres cremas y regalo", OfertasCombinadasDetalles = detalles };
            A.CallTo(() => servicio.BuscarOfertasCombinadas("A")).Returns(new List<OfertaCombinada> { oferta });
            foreach (string r in regalos)
            {
                A.CallTo(() => servicio.BuscarOfertasCombinadas(r)).Returns(new List<OfertaCombinada> { oferta });
            }
        }

        [TestMethod]
        public void FaltaUnaUnidadDeLaParteCobrada_SugiereAmpliar()
        {
            OfertaTresDeAConB("B");

            SugerenciaOfertaDTO s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 2, 10)), servicio, Acepta).Single();

            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_AMPLIAR_CANTIDAD_COMBINADA, s.Tipo);
            Assert.AreEqual(GestorSugerenciasOfertas.ACCION_MODIFICAR_LINEA, s.Accion);
            Assert.AreEqual("A", s.Producto);
            Assert.AreEqual(2, s.CantidadActual);
            Assert.AreEqual(3, s.CantidadSugerida);
            Assert.AreEqual(1, s.CantidadRegalo);
            Assert.AreEqual(300, s.OfertaCombinada);
            Assert.AreEqual("Con 1 unidad más del producto A completa la oferta «Tres cremas y regalo» y se lleva de regalo 1 unidad del producto B (REGALO B).", s.Texto);
        }

        [TestMethod]
        public void LejosDeLaParteCobrada_NoMolesta()
        {
            OfertaTresDeAConB("B");

            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 1, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void Ampliar_SinStockDelRegalo_NoSePromete()
        {
            OfertaTresDeAConB("B");
            A.CallTo(() => servicio.BuscarStockDisponibleParaRegalar("B", A<string>._)).Returns(0);

            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 2, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void GrupoDeAlternativas_ProponeLaPrimeraConStockYDiceEntreCualesElegir()
        {
            OfertaTresDeAConB("B1", "B2", "B3");
            A.CallTo(() => servicio.BuscarStockDisponibleParaRegalar("B1", A<string>._)).Returns(0);

            SugerenciaOfertaDTO s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 3, 10)), servicio, Acepta).Single();

            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_REGALO_COMBINADA_NO_APLICADO, s.Tipo);
            Assert.AreEqual("B2", s.Producto);
            CollectionAssert.AreEqual(new List<string> { "B2", "B3" }, s.Alternativas);
            StringAssert.Contains(s.Texto, "1 unidad a elegir entre los productos B2, B3");
        }

        [TestMethod]
        public void GrupoDeAlternativas_ConUnaSolaConStock_NoHayDondeElegir()
        {
            OfertaTresDeAConB("B1", "B2");
            A.CallTo(() => servicio.BuscarStockDisponibleParaRegalar("B1", A<string>._)).Returns(0);

            SugerenciaOfertaDTO s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 3, 10)), servicio, Acepta).Single();

            Assert.AreEqual("B2", s.Producto);
            Assert.IsNull(s.Alternativas);
        }

        [TestMethod]
        public void ConElRegaloDelGrupoYaPuesto_NoSugiereNada()
        {
            OfertaTresDeAConB("B1", "B2");

            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 3, 10), Linea("B2", 1, 0, id: 2, oferta: 1)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void OfertaDeRegalarMenorImporte_NoSeSugiere()
        {
            OfertaTresDeAConB("B");
            servicio.BuscarOfertasCombinadas("A")[0].RegalarMenorImporte = true;

            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 3, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void OfertaDeOtraEmpresa_NoCuenta()
        {
            OfertaTresDeAConB("B");
            servicio.BuscarOfertasCombinadas("A")[0].Empresa = "3";

            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("A", 3, 10)), servicio, Acepta).Count);
        }

        #endregion
    }
}
