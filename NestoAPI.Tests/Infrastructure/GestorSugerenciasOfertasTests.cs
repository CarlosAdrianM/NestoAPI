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
            A.CallTo(() => servicio.BuscarProducto("38093")).Returns(new Producto { Número = "38093", Nombre = "CHAMPU HIDRATANTE", Familia = "DeMarca", PVP = 10, Aplicar_Dto = true });
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

        // 23/09/26 (vendedor): el 45917 (Anubis) tiene Aplicar_Dto = 0 y Nesto/NestoApp le avisaban del 6+1
        // de su familia al meter 7 unidades. Sin descuento no hay N+M de familia; solo una oferta expresa.
        private void OfertaDeFamiliaAnubis(string producto, bool aplicarDto)
        {
            A.CallTo(() => servicio.BuscarProducto(producto)).Returns(new Producto { Número = producto, Nombre = "PACK SAPPHIRE", Familia = "Anubis", PVP = 20, Aplicar_Dto = aplicarDto });
            A.CallTo(() => servicio.BuscarOfertasPermitidas(producto)).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 50, Familia = "Anubis", CantidadConPrecio = 6, CantidadRegalo = 1 }
            });
        }

        [TestMethod]
        public void ProductoSinDescuento_NoSeLeSugiereElNMasMDeSuFamilia()
        {
            OfertaDeFamiliaAnubis("45917", aplicarDto: false);

            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("45917", 7, 20)), servicio, Acepta);

            Assert.AreEqual(0, s.Count);
        }

        [TestMethod]
        public void ProductoConDescuento_SiSeLeSugiereElNMasMDeSuFamilia()
        {
            OfertaDeFamiliaAnubis("45918", aplicarDto: true);

            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("45918", 7, 20)), servicio, Acepta);

            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_OFERTA_NO_APLICADA, s.Single().Tipo);
        }

        [TestMethod]
        public void ProductoSinDescuentoConOfertaExpresaSuya_SiSeSugiere()
        {
            // 40640/40642 (5+1) y 44731 (10+1) tienen Aplicar_Dto = 0 y una oferta dada de alta para ellos.
            A.CallTo(() => servicio.BuscarProducto("40640")).Returns(new Producto { Número = "40640", Nombre = "KIT WATERPROOF", Familia = "Otra", PVP = 10, Aplicar_Dto = false });
            A.CallTo(() => servicio.BuscarOfertasPermitidas("40640")).Returns(new List<OfertaPermitida>
            {
                new OfertaPermitida { NºOrden = 308, Número = "40640", CantidadConPrecio = 5, CantidadRegalo = 1 }
            });

            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("40640", 5, 10)), servicio, Acepta);

            Assert.AreEqual(308, s.Single().Oferta);
        }

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

        // ===== Corte 3: ofertas escalonadas (#226) =====

        /// <summary>Oferta escalonada Allure: 44707 y 44708 a 44,95 € de base; tramos 4→10 %, 6→25 %.</summary>
        private void ConEscalonadaAllure(params string[] productos)
        {
            var oferta = new OfertaEscalonada { Id = 5, Empresa = "1", Nombre = "Allure" };
            foreach (string p in productos)
            {
                oferta.OfertasEscalonadasProductos.Add(new OfertaEscalonadaProducto { OfertaId = 5, Producto = p, PrecioBase = 44.95M });
            }
            oferta.OfertasEscalonadasTramos.Add(new OfertaEscalonadaTramo { OfertaId = 5, CantidadMinima = 4, Descuento = 0.10M });
            oferta.OfertasEscalonadasTramos.Add(new OfertaEscalonadaTramo { OfertaId = 5, CantidadMinima = 6, Descuento = 0.25M });
            foreach (string p in productos)
            {
                A.CallTo(() => servicio.BuscarOfertasEscalonadas(p)).Returns(new List<OfertaEscalonada> { oferta });
            }
        }

        private static LineaPedidoVentaDTO LineaConDto(string producto, int cantidad, decimal precio, decimal descuentoLinea, int id) => new LineaPedidoVentaDTO
        {
            id = id,
            Producto = producto,
            Cantidad = cantidad,
            PrecioUnitario = precio,
            DescuentoLinea = descuentoLinea,
            AplicarDescuento = true,
            tipoLinea = Constantes.TiposLineaVenta.PRODUCTO,
            almacen = "ALG"
        };

        [TestMethod]
        public void Escalonada_AlcanzaElTramoYPagaTarifa_AvisaDelDescuentoNoAplicadoYLoValidaConElDescuentoPuesto()
        {
            ConEscalonadaAllure("44707", "44708");
            PedidoVentaDTO validado = null;
            // 4 de un producto y 2 del otro = 6 unidades: tramo del 25 %, y las dos líneas van a tarifa.
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(
                Pedido(Linea("44707", 4, 44.95M, 1), Linea("44708", 2, 44.95M, 2)), servicio,
                p => { validado = p; return new RespuestaValidacion { ValidacionSuperada = true }; });

            List<SugerenciaOfertaDTO> descuento = s.Where(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO).ToList();
            Assert.AreEqual(2, descuento.Count, "Un aviso por producto que paga de más");
            SugerenciaOfertaDTO a = descuento.Single(x => x.Producto == "44707");
            Assert.AreEqual(0.25M, a.Descuento);
            Assert.AreEqual(4, a.CantidadActual);
            Assert.AreEqual(4, a.CantidadSugerida, "No hay que añadir unidades, solo aplicar el descuento");
            Assert.AreEqual(5, a.OfertaEscalonada);
            StringAssert.Contains(a.Texto, "6 unidades");
            StringAssert.Contains(a.Texto, "25 %");
            StringAssert.Contains(a.Texto, "Allure");
            // El pedido hipotético lleva las dos líneas al suelo del tramo: base 44,95 con 25 % de línea.
            Assert.IsNotNull(validado);
            Assert.IsTrue(validado.Lineas.All(l => l.PrecioUnitario == 44.95M && l.DescuentoLinea == 0.25M && l.AplicarDescuento));
            Assert.IsFalse(s.Any(x => x.Tipo == GestorSugerenciasOfertas.TIPO_AMPLIAR_CANTIDAD_ESCALONADA), "Ya está en el tramo más alto");
        }

        [TestMethod]
        public void Escalonada_YaLlevaElDescuentoDelTramo_NoAvisa()
        {
            ConEscalonadaAllure("44707", "44708");
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(
                Pedido(LineaConDto("44707", 4, 44.95M, 0.25M, 1), LineaConDto("44708", 2, 44.95M, 0.25M, 2)), servicio, Acepta);

            Assert.IsFalse(s.Any(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO));
        }

        [TestMethod]
        public void Escalonada_PrecioRebajadoDirectamenteEnLaLinea_TambienCuentaComoAplicado()
        {
            ConEscalonadaAllure("44707");
            // 44,95 × 0,75 = 33,7125: el vendedor teclea 33,71 (redondeo dentro de la tolerancia).
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("44707", 6, 33.71M)), servicio, Acepta);

            Assert.IsFalse(s.Any(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO));
        }

        [TestMethod]
        public void Escalonada_SoloAvisaDelProductoQuePagaDeMas()
        {
            ConEscalonadaAllure("44707", "44708");
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(
                Pedido(LineaConDto("44707", 4, 44.95M, 0.25M, 1), Linea("44708", 2, 44.95M, 2)), servicio, Acepta);

            SugerenciaOfertaDTO d = s.Single(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO);
            Assert.AreEqual("44708", d.Producto);
        }

        [TestMethod]
        public void Escalonada_FaltaPocoParaElSiguienteTramo_SugiereAmpliarEnElProductoConMasUnidades()
        {
            ConEscalonadaAllure("44707", "44708");
            // 2 + 1 = 3 unidades: al tramo de 4 le falta 1 (dentro de la mitad).
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(
                Pedido(LineaConDto("44707", 2, 44.95M, 0, 1), LineaConDto("44708", 1, 44.95M, 0, 2)), servicio, Deniega);

            SugerenciaOfertaDTO amp = s.Single();
            Assert.AreEqual(GestorSugerenciasOfertas.TIPO_AMPLIAR_CANTIDAD_ESCALONADA, amp.Tipo);
            Assert.AreEqual("44707", amp.Producto, "El que más unidades tiene");
            Assert.AreEqual(2, amp.CantidadActual);
            Assert.AreEqual(3, amp.CantidadSugerida);
            Assert.AreEqual(0.10M, amp.Descuento);
            Assert.AreEqual(5, amp.OfertaEscalonada);
            StringAssert.Contains(amp.Texto, "1 unidad más");
            StringAssert.Contains(amp.Texto, "10 %");
        }

        [TestMethod]
        public void Escalonada_EnUnTramoYCercaDelSiguiente_AvisaDeLosDos()
        {
            ConEscalonadaAllure("44707");
            // 5 unidades a tarifa: tiene derecho al 10 % (tramo 4) y con 1 más pasa al 25 %.
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("44707", 5, 44.95M)), servicio, Acepta);

            Assert.AreEqual(0.10M, s.Single(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO).Descuento);
            SugerenciaOfertaDTO amp = s.Single(x => x.Tipo == GestorSugerenciasOfertas.TIPO_AMPLIAR_CANTIDAD_ESCALONADA);
            Assert.AreEqual(0.25M, amp.Descuento);
            Assert.AreEqual(6, amp.CantidadSugerida);
            StringAssert.Contains(amp.Texto, "ahora tienes el 10 %");
        }

        [TestMethod]
        public void Escalonada_LejosDelTramo_NoMolesta()
        {
            ConEscalonadaAllure("44707");
            // 1 de 4: falta más de la mitad.
            Assert.AreEqual(0, GestorSugerenciasOfertas.Calcular(Pedido(Linea("44707", 1, 44.95M)), servicio, Acepta).Count);
        }

        [TestMethod]
        public void Escalonada_LasUnidadesRegaladasNoCuentan()
        {
            ConEscalonadaAllure("44707");
            // 3 cobradas + 3 regaladas: siguen siendo 3 a efectos de tramo (nada de 25 %).
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(
                Pedido(Linea("44707", 3, 44.95M, 1), Linea("44707", 3, 0, 2)), servicio, Acepta);

            Assert.IsFalse(s.Any(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO));
        }

        [TestMethod]
        public void Escalonada_LaValidacionLoRechaza_NoSeSugiereElDescuento()
        {
            ConEscalonadaAllure("44707");
            List<SugerenciaOfertaDTO> s = GestorSugerenciasOfertas.Calcular(Pedido(Linea("44707", 6, 44.95M)), servicio, Deniega);

            Assert.IsFalse(s.Any(x => x.Tipo == GestorSugerenciasOfertas.TIPO_DESCUENTO_NO_APLICADO));
        }

        [TestMethod]
        public void Escalonada_LasLineasOriginalesNoSeTocan()
        {
            ConEscalonadaAllure("44707");
            LineaPedidoVentaDTO linea = Linea("44707", 6, 44.95M);
            _ = GestorSugerenciasOfertas.Calcular(Pedido(linea), servicio, Acepta);

            Assert.AreEqual(0M, linea.DescuentoLinea);
            Assert.AreEqual(44.95M, linea.PrecioUnitario);
        }
    }
}
