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
    /// NestoAPI#457 (corte 1): sugerencias de ofertas N+M (OfertasPermitidas) que el pedido podría
    /// aplicar y no aplica. La validación del pedido hipotético se inyecta: aquí se prueba la regla,
    /// el circuito de validación tiene sus propios tests (GestorPreciosTests).
    /// </summary>
    [TestClass]
    public class GestorSugerenciasOfertasTests
    {
        private IServicioPrecios servicio;
        private static readonly System.Func<PedidoVentaDTO, RespuestaValidacion> Acepta = p => new RespuestaValidacion { ValidacionSuperada = true };
        private static readonly System.Func<PedidoVentaDTO, RespuestaValidacion> Deniega = p => new RespuestaValidacion { ValidacionSuperada = false, Motivo = "Oferta no autorizada" };

        [TestInitialize]
        public void Setup()
        {
            servicio = A.Fake<IServicioPrecios>();
            A.CallTo(() => servicio.BuscarProducto("38093")).Returns(new Producto { Número = "38093", Nombre = "CHAMPU HIDRATANTE", Familia = "DeMarca", PVP = 10 });
            A.CallTo(() => servicio.BuscarProducto("SINOF")).Returns(new Producto { Número = "SINOF", Nombre = "OTRO", Familia = "Otra", PVP = 10 });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("38093")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 7, Número = "38093", CantidadConPrecio = 6, CantidadRegalo = 1 }
            });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("SINOF")).Returns(new List<OfertaPermitida>());
        }

        private static PedidoVentaDTO Pedido(params LineaPedidoVentaDTO[] lineas) => new PedidoVentaDTO
        {
            empresa = "1",
            cliente = "15191",
            contacto = "0",
            Lineas = lineas.ToList()
        };

        private static LineaPedidoVentaDTO Linea(string producto, int cantidad, decimal precio, int id = 1) => new LineaPedidoVentaDTO
        {
            id = id,
            Producto = producto,
            Cantidad = cantidad,
            PrecioUnitario = precio,
            tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            almacen = "ALG"
        };

        [TestMethod]
        public void CincoUnidadesConUn6Mas1_SugiereAmpliarAUnaMas()
        {
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 5, 10)), servicio, Acepta);

            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_AMPLIAR_CANTIDAD, s[0].Tipo);
            Assert.AreEqual("38093", s[0].Producto);
            Assert.AreEqual(5, s[0].CantidadActual);
            Assert.AreEqual(6, s[0].CantidadSugerida);
            Assert.AreEqual(1, s[0].CantidadRegalo);
            Assert.AreEqual(7, s[0].Oferta);
            StringAssert.Contains(s[0].Texto, "1 unidad más");
        }

        [TestMethod]
        public void SeisUnidadesSinRegalo_AvisaDeOfertaNoAplicada()
        {
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 6, 10)), servicio, Acepta);

            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_OFERTA_NO_APLICADA, s[0].Tipo);
            Assert.AreEqual(6, s[0].CantidadSugerida, "No hay que añadir cobradas");
            Assert.AreEqual(1, s[0].CantidadRegalo);
        }

        [TestMethod]
        public void TreceUnidades_ElRegaloEsPorCadaTramoCompleto()
        {
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 13, 10)), servicio, Acepta);
            Assert.AreEqual(2, s.Single().CantidadRegalo, "13 unidades = dos tramos de 6");
        }

        [TestMethod]
        public void ConElRegaloYaPuesto_NoSugiereNada()
        {
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 6, 10), Linea("38093", 1, 0, id: 2)), servicio, Acepta);
            Assert.AreEqual(0, s.Count);
        }

        [TestMethod]
        public void LejosDelTramo_NoMolesta()
        {
            // 2 de 6: falta más de la mitad; no se sugiere.
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 2, 10)), servicio, Acepta).Count);
            // 3 de 6: justo la mitad, sí.
            Assert.AreEqual(1, GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 3, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void SinOfertas_ODenegada_ODeOtroCliente_NoSugiere()
        {
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 6, 10)), servicio, Acepta).Count);

            A.CallTo(() => servicio.BuscarOfertasPermitidas("38093")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 8, Número = "38093", CantidadConPrecio = 6, CantidadRegalo = 1, Denegar = true },
                new OfertaPermitida { NºOrden = 9, Número = "38093", CantidadConPrecio = 3, CantidadRegalo = 1, Cliente = "99999" }
            });
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 6, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void OfertaDeFamilia_AplicaSiNoHayExpresaDelProducto_YRespetaElFiltroPorNombre()
        {
            A.CallTo(() => servicio.BuscarOfertasPermitidas("38093")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 10, Familia = "DeMarca", CantidadConPrecio = 4, CantidadRegalo = 1, FiltroProducto = "CHAMPU" },
                new OfertaPermitida { NºOrden = 11, Familia = "DeMarca", CantidadConPrecio = 2, CantidadRegalo = 1, FiltroProducto = "MASCARILLA" }
            });

            SugerenciaOfertaDTO s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 4, 10)), servicio, Acepta).Single();

            Assert.AreEqual(10, s.Oferta, "La de MASCARILLA no casa con el nombre del producto");
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_OFERTA_NO_APLICADA, s.Tipo);
        }

        [TestMethod]
        public void NoSugiereLoQueElPedidoRechazaria()
        {
            PedidoVentaDTO validado = null;
            System.Func<PedidoVentaDTO, RespuestaValidacion> captura = p => { validado = p; return Deniega(p); };

            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("38093", 5, 10)), servicio, captura);

            Assert.AreEqual(0, s.Count);
            Assert.IsNotNull(validado, "Se valida el pedido hipotético");
            // El hipotético lleva la línea ampliada a 6 y la de regalo a 0 €, sin tocar el original.
            List<LineaPedidoVentaDTO> lineas = validado.Lineas.ToList();
            Assert.AreEqual(2, lineas.Count);
            Assert.AreEqual(6, lineas[0].Cantidad);
            Assert.AreEqual(60m, lineas[0].BaseImponible, "10 € × 6 (la base se calcula sola)");
            Assert.AreEqual(1, lineas[1].Cantidad);
            Assert.AreEqual(0m, lineas[1].BaseImponible);
            Assert.AreEqual(1, lineas[1].oferta);
        }

        [TestMethod]
        public void LasLineasOriginalesNoSeTocan()
        {
            LineaPedidoVentaDTO linea = Linea("38093", 5, 10);
            PedidoVentaDTO pedido = Pedido(linea);

            GestorSugerenciasOfertas.Calcular(pedido, servicio, Acepta);

            Assert.AreEqual(5, linea.Cantidad);
            Assert.AreEqual(1, pedido.Lineas.Count);
        }
        // ===== Corte 2: regalo por importe de pedido =====

        private void ConRegalo(string producto, decimal importe, short cantidad, string empresa = "1")
        {
            var lista = servicio.BuscarRegalosPorImportePedidoVigentes() ?? new List<RegaloImportePedido>();
            lista = new List<RegaloImportePedido>(lista) { new RegaloImportePedido { Empresa = empresa, Producto = producto, ImportePedido = importe, Cantidad = cantidad } };
            A.CallTo(() => servicio.BuscarRegalosPorImportePedidoVigentes()).Returns(lista);
        }

        [TestMethod]
        public void RegaloPorImporte_ElPedidoLlegaYNoLoLleva_AvisaConElTramoQueMasDa()
        {
            ConRegalo("REG1", 200, 1);
            ConRegalo("REG1", 400, 2);
            // 45 × 10 = 450 € de base imponible: llega a los dos tramos, el de 400 da más.
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 45, 10)), servicio, Acepta);

            SugerenciaOfertaDTO r = s.Single(x => x.Tipo == GestorSugerenciasOfertas.TIPO_REGALO_NO_APLICADO);
            Assert.AreEqual("REG1", r.Producto);
            Assert.AreEqual(2, r.CantidadRegalo);
            Assert.AreEqual(400M, r.ImportePedido);
            Assert.AreEqual(0M, r.ImporteQueFalta);
            StringAssert.Contains(r.Texto, "400,00");
            StringAssert.Contains(r.Texto, "2 unidades del producto REG1 de regalo");
        }

        [TestMethod]
        public void RegaloPorImporte_FaltaPoco_SugiereAmpliarConLoQueFalta()
        {
            ConRegalo("REG1", 200, 1);
            // 17 × 10 = 170 €: faltan 30 €, dentro del 25 % de 200 (50 €).
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 17, 10)), servicio, Deniega);

            SugerenciaOfertaDTO r = s.Single();
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_AMPLIAR_IMPORTE, r.Tipo);
            Assert.AreEqual(30M, r.ImporteQueFalta);
            Assert.AreEqual(200M, r.ImportePedido);
            Assert.AreEqual(1, r.CantidadRegalo);
            StringAssert.Contains(r.Texto, "Añadiendo 30,00");
            StringAssert.Contains(r.Texto, "1 unidad del producto REG1");
        }

        [TestMethod]
        public void RegaloPorImporte_FaltaMucho_NoMolesta()
        {
            ConRegalo("REG1", 200, 1);
            // 10 × 10 = 100 €: faltan 100 €, más del 25 %.
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 10, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void RegaloPorImporte_YaLoLleva_NoSugiereNada()
        {
            ConRegalo("REG1", 200, 1);
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 30, 10), Linea("REG1", 1, 0, 2)), servicio, Acepta);
            Assert.IsFalse(s.Any(x => x.Producto == "REG1"));
        }

        [TestMethod]
        public void RegaloPorImporte_LaValidacionLoRechaza_NoSeSugiere()
        {
            ConRegalo("REG1", 200, 1);
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 30, 10)), servicio, Deniega).Count);
        }

        [TestMethod]
        public void RegaloPorImporte_DeOtraEmpresa_NoCuenta()
        {
            ConRegalo("REG1", 200, 1, empresa: "3");
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 30, 10)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void RegaloPorImporte_LaLineaDeRegaloHipoteticaVaAPrecioCeroConLosDatosDeLaPrimeraLinea()
        {
            ConRegalo("REG1", 200, 1);
            A.CallTo(() => servicio.BuscarProducto("REG1")).Returns(new Producto { Número = "REG1", Nombre = "NECESER", Grupo = "COS", SubGrupo = "MMP", PVP = 12 });
            PedidoVentaDTO validado = null;
            _ = GestorSugerenciasOfertas.Calcular(Pedido(Linea("SINOF", 30, 10)), servicio, p => { validado = p; return new RespuestaValidacion { ValidacionSuperada = true }; });

            LineaPedidoVentaDTO regalo = validado.Lineas.Last();
            Assert.AreEqual("REG1", regalo.Producto);
            Assert.AreEqual(0, regalo.id);
            Assert.AreEqual(1, regalo.Cantidad);
            Assert.AreEqual(0M, regalo.PrecioUnitario);
            Assert.AreEqual(0M, regalo.BaseImponible);
            Assert.AreEqual("ALG", regalo.almacen);
            Assert.AreEqual("COS", regalo.GrupoProducto);
            Assert.AreEqual(2, validado.Lineas.Count, "las líneas del pedido de verdad no se tocan");
        }

    }
}
