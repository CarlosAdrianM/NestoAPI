using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.PedidosVenta;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Tests.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#571 (cortes 1 y 2): «Recoge en tienda» traducido a ruta, sin portes ni comisión contra reembolso.</summary>
    [TestClass]
    public class RecogeEnTiendaTests
    {
        [TestCleanup]
        public void Limpiar()
        {
            GestorPortes.FechaActualParaPruebas = null;
            GestorPortes.IncrementoReembolsoParaPruebas = null;
        }

        private static LineaPedidoVentaDTO Linea(string almacen, short estado = Constantes.EstadosLineaVenta.PENDIENTE,
            byte tipo = Constantes.TiposLineaVenta.PRODUCTO, string producto = "17404")
        {
            return new LineaPedidoVentaDTO { almacen = almacen, estado = estado, tipoLinea = tipo, Producto = producto, Cantidad = 1 };
        }

        private static List<LineaPedidoVentaDTO> Lineas(params string[] almacenes)
            => almacenes.Select(a => Linea(a)).ToList();

        private static PedidoPortesInput InputContraReembolso(string ruta, decimal baseProductos = 30M)
        {
            return new PedidoPortesInput
            {
                CodigoPostal = "28110",
                Ruta = ruta,
                FormaPago = "EFC",
                PlazosPago = "CONTADO",
                PeriodoFacturacion = "NRM",
                Iva = Constantes.Empresas.IVA_POR_DEFECTO,
                BaseImponibleProductos = baseProductos
            };
        }

        #region TiendasRecogida.EsRutaDeRecogida

        [TestMethod]
        public void EsRutaDeRecogida_LasTresRutasConRellenoYMinusculas_SonDeRecogida()
        {
            Assert.IsTrue(TiendasRecogida.EsRutaDeRecogida("AM"));
            Assert.IsTrue(TiendasRecogida.EsRutaDeRecogida("am "));
            Assert.IsTrue(TiendasRecogida.EsRutaDeRecogida("ALC"));
            Assert.IsTrue(TiendasRecogida.EsRutaDeRecogida("REI"));
        }

        [TestMethod]
        public void EsRutaDeRecogida_OtrasRutasONula_NoSonDeRecogida()
        {
            Assert.IsFalse(TiendasRecogida.EsRutaDeRecogida("AT"));
            Assert.IsFalse(TiendasRecogida.EsRutaDeRecogida("FW"));
            Assert.IsFalse(TiendasRecogida.EsRutaDeRecogida("GLV"));
            Assert.IsFalse(TiendasRecogida.EsRutaDeRecogida(null));
            Assert.IsFalse(TiendasRecogida.EsRutaDeRecogida("  "));
        }

        #endregion

        #region GestorPortes: sin portes ni comisión en una ruta de recogida

        [TestMethod]
        public void CalcularPortes_RutaAMContraReembolsoSinLlegarAlMinimo_NiPortesNiComision()
        {
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;

            ResultadoPortes resultado = GestorPortes.CalcularPortes(InputContraReembolso("AM"));

            Assert.AreEqual(0M, resultado.ImportePortes);
            Assert.AreEqual(0M, resultado.ComisionReembolso);
            Assert.IsTrue(resultado.PortesGratis);
            Assert.AreEqual(0M, resultado.ImporteFaltaParaPortesGratis);
            Assert.IsTrue(resultado.EsContraReembolso, "La forma de pago sigue informándose");
        }

        [TestMethod]
        public void CalcularPortes_RutasALCyREIConRelleno_SinComision()
        {
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;

            Assert.AreEqual(0M, GestorPortes.CalcularPortes(InputContraReembolso("ALC")).ComisionReembolso);
            Assert.AreEqual(0M, GestorPortes.CalcularPortes(InputContraReembolso("REI ")).ComisionReembolso);
        }

        [TestMethod]
        public void CalcularPortes_RutaATContraReembolso_ComoHoyConPortesYComision()
        {
            // Regresión: ALG sin marcar (su ruta de siempre) no cambia.
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;

            ResultadoPortes resultado = GestorPortes.CalcularPortes(InputContraReembolso("AT"));

            Assert.IsTrue(resultado.ImportePortes > 0);
            Assert.IsFalse(resultado.PortesGratis);
            Assert.AreEqual(3M, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void CalcularPortes_OtraRutaSinPortesQueNoEsDeRecogida_SigueConComision()
        {
            // Solo cambian las tres rutas de recogida: el resto de rutas sin portes sigue llevando reembolso.
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;

            ResultadoPortes resultado = GestorPortes.CalcularPortes(InputContraReembolso("XX"));

            Assert.AreEqual(0M, resultado.ImportePortes);
            Assert.AreEqual(3M, resultado.ComisionReembolso);
        }

        [TestMethod]
        public void CalcularPortes_PlantillaConRecogeEnTiendaTrue_NiPortesNiComisionAunqueLaRutaSeaAT()
        {
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            PedidoPortesInput input = InputContraReembolso("AT");
            input.RecogeEnTienda = true;

            ResultadoPortes resultado = GestorPortes.CalcularPortes(input);

            Assert.AreEqual(0M, resultado.ImportePortes);
            Assert.AreEqual(0M, resultado.ComisionReembolso);
            Assert.IsTrue(resultado.PortesGratis);
        }

        [TestMethod]
        public void CalcularPortes_PlantillaConRecogeEnTiendaNullOFalse_ComoHoy()
        {
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            PedidoPortesInput sinMarca = InputContraReembolso("AT");
            PedidoPortesInput desmarcado = InputContraReembolso("AT");
            desmarcado.RecogeEnTienda = false;

            Assert.AreEqual(3M, GestorPortes.CalcularPortes(sinMarca).ComisionReembolso);
            Assert.IsTrue(GestorPortes.CalcularPortes(sinMarca).ImportePortes > 0);
            Assert.AreEqual(3M, GestorPortes.CalcularPortes(desmarcado).ComisionReembolso);
            Assert.IsTrue(GestorPortes.CalcularPortes(desmarcado).ImportePortes > 0);
        }

        [TestMethod]
        public void GestionarLineasPortes_MarcarQuitaPortesYComision_DesmarcarLosDevuelve()
        {
            GestorPortes.IncrementoReembolsoParaPruebas = 3M;
            var lineas = new List<LineaPedidoVentaDTO> { Linea("ALG") };

            // Pedido de Algete por su ruta (AT): lleva portes y comisión.
            _ = GestorPortes.GestionarLineasPortes(lineas, GestorPortes.CalcularPortes(InputContraReembolso("AT")), "G21", null);
            Assert.AreEqual(3, lineas.Count, "Producto + portes + comisión");

            // Marcar: ruta AM → se van los dos.
            _ = GestorPortes.GestionarLineasPortes(lineas, GestorPortes.CalcularPortes(InputContraReembolso("AM")), "G21", null);
            Assert.AreEqual(1, lineas.Count, "Solo queda el producto");

            // Desmarcar: vuelve su ruta → vuelven portes y comisión según importe y forma de pago.
            _ = GestorPortes.GestionarLineasPortes(lineas, GestorPortes.CalcularPortes(InputContraReembolso("AT")), "G21", null);
            Assert.AreEqual(3, lineas.Count);
            Assert.IsTrue(lineas.Any(l => l.texto == Constantes.Portes.TEXTO_COMISION_REEMBOLSO));
            Assert.IsTrue(lineas.Any(l => l.texto == "Portes"));
        }

        #endregion

        #region POST: AlCrear

        [TestMethod]
        public void AlCrear_SinMarca_NoTocaNada()
        {
            RecogeEnTienda.Resultado r = RecogeEnTienda.AlCrear(null, false, Lineas("ALC"));
            Assert.IsNull(r.Ruta);
            Assert.IsNull(r.Error);
        }

        [TestMethod]
        public void AlCrear_MarcadoEnCadaAlmacen_RutaDeSuTienda()
        {
            Assert.AreEqual("AM", RecogeEnTienda.AlCrear(true, false, Lineas("ALG")).Ruta);
            Assert.AreEqual("AM", RecogeEnTienda.AlCrear(true, false, Lineas("alg ")).Ruta);
            Assert.AreEqual("ALC", RecogeEnTienda.AlCrear(true, false, Lineas("ALC")).Ruta);
            Assert.AreEqual("REI", RecogeEnTienda.AlCrear(true, false, Lineas("REI")).Ruta);
        }

        [TestMethod]
        public void AlCrear_DesmarcadoEnAlgete_ComoHoy()
        {
            RecogeEnTienda.Resultado r = RecogeEnTienda.AlCrear(false, false, Lineas("ALG"));
            Assert.IsNull(r.Ruta);
            Assert.IsNull(r.Error);
        }

        [TestMethod]
        public void AlCrear_DesmarcadoEnAlcobendasOReina_Rechazado()
        {
            Assert.AreEqual("Los pedidos del almacén de Alcobendas se recogen siempre en la tienda: no se puede desmarcar «Recoge en tienda».",
                RecogeEnTienda.AlCrear(false, false, Lineas("ALC")).Error);
            Assert.AreEqual("Los pedidos del almacén de Reina se recogen siempre en la tienda: no se puede desmarcar «Recoge en tienda».",
                RecogeEnTienda.AlCrear(false, false, Lineas("REI")).Error);
        }

        [TestMethod]
        public void AlCrear_NotaDeEntrega_IgnoraLaMarca()
        {
            RecogeEnTienda.Resultado r = RecogeEnTienda.AlCrear(true, true, Lineas("ALG"));
            Assert.IsNull(r.Ruta);
            Assert.IsNull(r.Error);
        }

        [TestMethod]
        public void AlCrear_MarcadoConLineasDeVariosAlmacenes_Rechazado()
        {
            StringAssert.Contains(RecogeEnTienda.AlCrear(true, false, Lineas("ALG", "REI")).Error, "varios almacenes");
        }

        [TestMethod]
        public void AlCrear_MarcadoEnUnAlmacenQueNoEsTienda_Rechazado()
        {
            StringAssert.Contains(RecogeEnTienda.AlCrear(true, false, Lineas("XYZ")).Error, "no es una tienda de recogida");
        }

        [TestMethod]
        public void AlCrear_ElAlmacenLoDanLasLineasDeProductoVivas()
        {
            var lineas = new List<LineaPedidoVentaDTO>
            {
                Linea("ALG"),
                Linea("REI", Constantes.EstadosLineaVenta.ALBARAN),
                Linea("REI", tipo: Constantes.TiposLineaVenta.CUENTA_CONTABLE, producto: "62400003")
            };
            Assert.AreEqual("AM", RecogeEnTienda.AlCrear(true, false, lineas).Ruta);
        }

        #endregion

        #region PUT: CambioAlModificar y AlModificar

        [TestMethod]
        public void CambioAlModificar_SinMarca_NoHaceNada()
        {
            Assert.IsNull(RecogeEnTienda.CambioAlModificar(null, false, "AT"));
            Assert.IsNull(RecogeEnTienda.CambioAlModificar(null, false, "AM"));
        }

        [TestMethod]
        public void CambioAlModificar_MismoValorQueLaRutaGuardada_NoHaceNada()
        {
            Assert.IsNull(RecogeEnTienda.CambioAlModificar(true, false, "AM "));
            Assert.IsNull(RecogeEnTienda.CambioAlModificar(true, false, "REI"));
            Assert.IsNull(RecogeEnTienda.CambioAlModificar(false, false, "AT"));
        }

        [TestMethod]
        public void CambioAlModificar_ValorDistintoDeLaRutaGuardada_MarcaODesmarca()
        {
            Assert.AreEqual(true, RecogeEnTienda.CambioAlModificar(true, false, "AT"));
            Assert.AreEqual(false, RecogeEnTienda.CambioAlModificar(false, false, "AM"));
        }

        [TestMethod]
        public void CambioAlModificar_NotaDeEntrega_IgnoraLaMarca()
        {
            Assert.IsNull(RecogeEnTienda.CambioAlModificar(true, true, "AT"));
        }

        [TestMethod]
        public void AlModificar_MarcarEnAlgete_RutaAMSinLeerLaFicha()
        {
            bool leida = false;
            RecogeEnTienda.Resultado r = RecogeEnTienda.AlModificar(true, Lineas("ALG"), null, () => { leida = true; return "AT"; });
            Assert.AreEqual("AM", r.Ruta);
            Assert.IsNull(r.Error);
            Assert.IsFalse(leida);
        }

        [TestMethod]
        public void AlModificar_DesmarcarEnAlgete_VuelveLaRutaDeLaFicha()
        {
            RecogeEnTienda.Resultado r = RecogeEnTienda.AlModificar(false, Lineas("ALG"), null, () => "AT ");
            Assert.AreEqual("AT", r.Ruta);
            Assert.IsNull(r.Error);
        }

        [TestMethod]
        public void AlModificar_DesmarcarEnAlcobendasOReina_Rechazado()
        {
            StringAssert.StartsWith(RecogeEnTienda.AlModificar(false, Lineas("ALC"), null, () => "AT").Error,
                "Los pedidos del almacén de Alcobendas se recogen siempre en la tienda");
            StringAssert.StartsWith(RecogeEnTienda.AlModificar(false, Lineas("REI"), null, () => "AT").Error,
                "Los pedidos del almacén de Reina se recogen siempre en la tienda");
        }

        [TestMethod]
        public void AlModificar_DesmarcarConLaFichaEnRutaDeRecogidaOSinRuta_Rechazado()
        {
            StringAssert.Contains(RecogeEnTienda.AlModificar(false, Lineas("ALG"), null, () => "AM").Error, "es de recogida en tienda");
            StringAssert.Contains(RecogeEnTienda.AlModificar(false, Lineas("ALG"), null, () => null).Error, "no tiene ruta");
        }

        [TestMethod]
        public void AlModificar_ConPicking_RechazadoDiciendoPorQue()
        {
            var guardadas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Producto = "17404", Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 4567,
                    TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Almacén = "ALG" }
            };
            string motivo = CambioAlmacenPedido.Motivo(guardadas, false, new List<EnviosAgencia>(), null, RecogeEnTienda.PREFIJO_BLOQUEO);

            RecogeEnTienda.Resultado r = RecogeEnTienda.AlModificar(true, Lineas("ALG"), motivo, () => "AT");

            Assert.IsNull(r.Ruta);
            StringAssert.StartsWith(r.Error, "No se puede cambiar «Recoge en tienda»: ");
            StringAssert.Contains(r.Error, "picking");
        }

        [TestMethod]
        public void AlModificar_ConEtiquetaImpresa_RechazadoDiciendoPorQue()
        {
            var guardadas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Producto = "17404", Estado = Constantes.EstadosLineaVenta.PENDIENTE, Picking = 0,
                    TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Almacén = "ALG" }
            };
            var envios = new List<EnviosAgencia>
            {
                new EnviosAgencia { CodigoBarras = "123456789", Estado = Constantes.Agencias.ESTADO_EN_CURSO }
            };
            string motivo = CambioAlmacenPedido.Motivo(guardadas, false, envios, null, RecogeEnTienda.PREFIJO_BLOQUEO);

            RecogeEnTienda.Resultado r = RecogeEnTienda.AlModificar(false, Lineas("ALG"), motivo, () => "AT");

            StringAssert.StartsWith(r.Error, "No se puede cambiar «Recoge en tienda»: ");
            StringAssert.Contains(r.Error, "etiqueta");
        }

        [TestMethod]
        public void MotivoCambioAlmacen_SinPrefijo_SigueIgual()
        {
            var guardadas = new List<LinPedidoVta>
            {
                new LinPedidoVta { Nº_Orden = 1, Producto = "17404", Estado = Constantes.EstadosLineaVenta.EN_CURSO, Picking = 4567,
                    TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Almacén = "ALG" }
            };
            StringAssert.StartsWith(CambioAlmacenPedido.Motivo(guardadas, false, new List<EnviosAgencia>(), null),
                "No se puede cambiar el almacén del pedido: ");
        }

        #endregion

        #region DTO

        [TestMethod]
        public void PedidoVentaDTO_AlLeer_RecogeEnTiendaSaleDeLaRuta()
        {
            Assert.AreEqual(true, new PedidoVentaDTO { ruta = "AM " }.RecogeEnTienda);
            Assert.AreEqual(false, new PedidoVentaDTO { ruta = "AT" }.RecogeEnTienda);
            Assert.AreEqual(false, new PedidoVentaDTO().RecogeEnTienda, "Siempre con valor");

            string json = JsonConvert.SerializeObject(new PedidoVentaDTO { ruta = "REI" });
            StringAssert.Contains(json, "\"RecogeEnTienda\":true");
            Assert.IsFalse(json.Contains("RecogeEnTiendaRecibido"));
        }

        [TestMethod]
        public void PedidoVentaDTO_AlEscribir_LoRecibidoSeGuardaAparte()
        {
            PedidoVentaDTO sinCampo = JsonConvert.DeserializeObject<PedidoVentaDTO>("{\"ruta\":\"AT\"}");
            PedidoVentaDTO marcado = JsonConvert.DeserializeObject<PedidoVentaDTO>("{\"ruta\":\"AT\",\"RecogeEnTienda\":true}");
            PedidoVentaDTO conNull = JsonConvert.DeserializeObject<PedidoVentaDTO>("{\"ruta\":\"AM\",\"RecogeEnTienda\":null}");

            Assert.IsNull(sinCampo.RecogeEnTiendaRecibido, "Nesto y la NestoApp publicadas no lo mandan: no se toca nada");
            Assert.AreEqual(true, marcado.RecogeEnTiendaRecibido);
            Assert.AreEqual(false, marcado.RecogeEnTienda, "Al leer manda la ruta, hasta que el servidor la cambie");
            Assert.IsNull(conNull.RecogeEnTiendaRecibido);
        }

        #endregion
    }
}
