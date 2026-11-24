using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.AlbaranesVenta;
using NestoAPI.Infraestructure.Facturas;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Infraestructure.Rectificativas;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.Rectificativas;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;
using Regla = NestoAPI.Infraestructure.Rectificativas.ReglaRectificativaDeRectificativa;

namespace NestoAPI.Tests.Infrastructure.Rectificativas
{
    /// <summary>
    /// NestoAPI#892 (caso RV2600189, 24/11/26): Alfredo abonó la NV2618549 con la RV2600186 y al día siguiente deshizo el abono
    /// copiando la RV2600186 invertida DENTRO del mismo pedido 931334, de serie RV. Salió la RV2600189 en positivo y sin
    /// vinculaciones, y Verifactu no la enviaba. Rectificar una rectificativa se permite y se declara vinculada a la que corrige.
    /// </summary>
    [TestClass]
    public class RectificativaDeRectificativaTests
    {
        private const string EMPRESA = "1";
        private const string CLIENTE = "10714";
        private const int PEDIDO = 931334;
        private const string ABONO = "RV2600186";
        private const string DESHACE = "RV2600189";

        [TestCleanup]
        public void Cleanup()
        {
            DeduplicadorErroresVerifactu.Reset();
        }

        #region Reglas puras

        [TestMethod]
        public void EsRectificarRectificativa_SoloAlInvertirUnaRectificativaHaciaOtraSerieRectificativa()
        {
            var invertir = new CopiarFacturaRequest { InvertirCantidades = true };

            Assert.IsTrue(Regla.EsRectificarRectificativa(invertir, "RV", "RV"), "RV2600189: la RV invertida dentro de su pedido RV");
            Assert.IsTrue(Regla.EsRectificarRectificativa(invertir, "RC", "RC"));
            Assert.IsFalse(Regla.EsRectificarRectificativa(invertir, "RV", "NV"), "Pedido nuevo en NV: es deshacer el abono (11/11/26), no esto");
            Assert.IsFalse(Regla.EsRectificarRectificativa(invertir, "NV", "RV"), "El abono de siempre");
            Assert.IsFalse(Regla.EsRectificarRectificativa(invertir, "GB", "GB"));
            Assert.IsFalse(Regla.EsRectificarRectificativa(new CopiarFacturaRequest { InvertirCantidades = false }, "RV", "RV"),
                "Copiar un abono sin invertir sigue siendo un abono (en negativo)");
        }

        [TestMethod]
        public void SeVincula_LoDeSiempreLasNegativas_AlRectificarUnaRectificativaLasPositivas()
        {
            var negativa = new LineaCopiadaDTO { CantidadCopiada = -2 };
            var positiva = new LineaCopiadaDTO { CantidadCopiada = 2 };

            Assert.IsTrue(Regla.SeVincula(negativa, false));
            Assert.IsFalse(Regla.SeVincula(positiva, false), "Una rectificativa normal no vincula sus positivas (como siempre)");
            Assert.IsTrue(Regla.SeVincula(positiva, true));
            Assert.IsFalse(Regla.SeVincula(negativa, true));
        }

        private static Regla.LineaPositiva Positiva(int linea, string producto, decimal cantidad)
            => new Regla.LineaPositiva { NumeroLinea = linea, Producto = producto, Cantidad = cantidad };

        private static Regla.LineaAbonoAnterior Anterior(string factura, DateTime fecha, int pedido, int linea, string producto, decimal cantidad)
            => new Regla.LineaAbonoAnterior { Factura = factura, Fecha = fecha, Pedido = pedido, Linea = linea, Producto = producto, Cantidad = cantidad };

        [TestMethod]
        public void Casar_CasoRV2600189_CadaPositivaConSuLineaDeLaRV2600186()
        {
            DateTime ayer = DateTime.Today.AddDays(-1);
            List<Regla.Vinculacion> vinculaciones = Regla.Casar(
                new[] { Positiva(4, "P1", 1), Positiva(5, "P2", 2), Positiva(6, "P3", 1) },
                new[]
                {
                    Anterior(ABONO, ayer, PEDIDO, 1, "P1    ", -1),
                    Anterior(ABONO, ayer, PEDIDO, 2, "P2", -2),
                    Anterior(ABONO, ayer, PEDIDO, 3, "P3", -1)
                },
                PEDIDO);

            Assert.IsNotNull(vinculaciones);
            Assert.AreEqual(3, vinculaciones.Count);
            CollectionAssert.AreEqual(new[] { 4, 5, 6 }, vinculaciones.Select(v => v.NumeroLinea).ToList());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, vinculaciones.Select(v => v.LineaOriginal).ToList());
            Assert.IsTrue(vinculaciones.All(v => v.FacturaOriginal == ABONO));
            CollectionAssert.AreEqual(new[] { 1M, 2M, 1M }, vinculaciones.Select(v => v.Cantidad).ToList(), "CantidadRectificada en positivo, como el script");
        }

        [TestMethod]
        public void Casar_GanaLaDelMismoPedidoAunqueHayaOtraMasReciente()
        {
            List<Regla.Vinculacion> vinculaciones = Regla.Casar(
                new[] { Positiva(4, "P1", 1) },
                new[]
                {
                    Anterior("RV2600200", DateTime.Today, 999999, 1, "P1", -1),
                    Anterior(ABONO, DateTime.Today.AddDays(-1), PEDIDO, 1, "P1", -1)
                },
                PEDIDO);

            Assert.AreEqual(ABONO, vinculaciones.Single().FacturaOriginal);
        }

        [TestMethod]
        public void Casar_SinNingunaDelMismoPedido_LaMasReciente()
        {
            List<Regla.Vinculacion> vinculaciones = Regla.Casar(
                new[] { Positiva(1, "P1", 1) },
                new[]
                {
                    Anterior("RV2600100", DateTime.Today.AddDays(-30), 1, 1, "P1", -1),
                    Anterior("RV2600180", DateTime.Today.AddDays(-2), 2, 7, "P1", -1)
                },
                PEDIDO);

            Assert.AreEqual("RV2600180", vinculaciones.Single().FacturaOriginal);
            Assert.AreEqual(7, vinculaciones.Single().LineaOriginal);
        }

        [TestMethod]
        public void Casar_DosDelMismoDiaQueCasanYNingunaDelMismoPedido_NoSeAdivina()
        {
            List<Regla.Vinculacion> vinculaciones = Regla.Casar(
                new[] { Positiva(1, "P1", 1) },
                new[]
                {
                    Anterior("RV2600180", DateTime.Today.AddDays(-2), 1, 1, "P1", -1),
                    Anterior("RV2600181", DateTime.Today.AddDays(-2), 2, 1, "P1", -1)
                },
                PEDIDO);

            Assert.IsNull(vinculaciones, "Mejor no curar que curar mal");
        }

        [TestMethod]
        public void Casar_SiAlgunaPositivaNoCasaEnLaMismaRectificativa_NoSeCuraNada()
        {
            DateTime ayer = DateTime.Today.AddDays(-1);
            Assert.IsNull(Regla.Casar(
                new[] { Positiva(4, "P1", 1), Positiva(5, "P2", 2) },
                new[] { Anterior(ABONO, ayer, PEDIDO, 1, "P1", -1), Anterior(ABONO, ayer, PEDIDO, 2, "P2", -1) },
                PEDIDO), "La cantidad tiene que ser exactamente la contraria");
            Assert.IsNull(Regla.Casar(
                new[] { Positiva(4, "P1", 1), Positiva(5, "P2", 1) },
                new[] { Anterior(ABONO, ayer, PEDIDO, 1, "P1", -1), Anterior("RV2600150", ayer, 1, 1, "P2", -1) },
                PEDIDO), "Repartida entre dos rectificativas no se cura");
            Assert.IsNull(Regla.Casar(
                new[] { Positiva(4, "P1", 1), Positiva(5, "P1", 1) },
                new[] { Anterior(ABONO, ayer, PEDIDO, 1, "P1", -1) },
                PEDIDO), "Cada línea del abono se usa una sola vez");
        }

        [TestMethod]
        public void Casar_LoQueYaCorrigeOtroDocumento_NoCuenta()
        {
            DateTime ayer = DateTime.Today.AddDays(-1);
            List<Regla.Vinculacion> vinculaciones = Regla.Casar(
                new[] { Positiva(4, "P1", 1) },
                new[] { Anterior(ABONO, ayer, PEDIDO, 1, "P1", -1), Anterior("RV2600100", ayer.AddDays(-10), 5, 3, "P1", -1) },
                PEDIDO,
                new HashSet<string> { Regla.Clave(ABONO + " ", 1) });

            Assert.AreEqual("RV2600100", vinculaciones.Single().FacturaOriginal, "La RV2600186 ya está deshecha: no se deshace dos veces");
        }

        [TestMethod]
        public void TextoRectificativaEnPositivo_NoDiceQueSeaUnaDevolucion()
        {
            Assert.IsTrue(VerifactuJobsService.EsRectificativaEnPositivo(new short?[] { 1, 2, null, 0 }));
            Assert.IsFalse(VerifactuJobsService.EsRectificativaEnPositivo(new short?[] { -1, 2 }), "Mixta: no es este caso");
            Assert.IsFalse(VerifactuJobsService.EsRectificativaEnPositivo(new short?[] { -1 }), "La devolución de siempre");
            Assert.IsFalse(VerifactuJobsService.EsRectificativaEnPositivo(new short?[] { null }));

            string texto = VerifactuJobsService.TextoRectificativaEnPositivoSinOrigen(new DateTime(2026, 11, 24));
            Assert.IsFalse(texto.Contains("devolución"), texto);
            StringAssert.Contains(texto, "abono");
            StringAssert.Contains(texto, "avise a Informática");
            StringAssert.Contains(texto, "25/11/2026");
        }

        #endregion

        #region Copiar en el acto y pendiente (GestorCopiaPedidos)

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        /// <summary>Un DbSet falso sobre una lista viva: lo que se añade se ve en las consultas siguientes.</summary>
        private static DbSet<T> SetVivo<T>(List<T> datos) where T : class
        {
            var set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            ConfigurarFakeDbSet(set, datos.AsQueryable());
            A.CallTo(() => set.Add(A<T>._)).ReturnsLazily((T e) => { datos.Add(e); return e; });
            return set;
        }

        private sealed class Escenario
        {
            public NVEntities Db;
            public IServicioFacturas ServicioFacturas;
            public IServicioAlbaranesVenta ServicioAlbaranes;
            public IAlmacenRectificativasPendientes Pendientes;
            public List<LinPedidoVta> Lineas;
            public List<LinFacturaVtaRectificacion> Vinculaciones = new List<LinFacturaVtaRectificacion>();

            public GestorCopiaPedidos Gestor() => new GestorCopiaPedidos(Db, ServicioPedidos, ServicioAlbaranes, ServicioFacturas, Pendientes);
            public IServicioPedidosVenta ServicioPedidos;
        }

        private static LinPedidoVta LineaAbono(int orden, string producto, short cantidad, decimal precio) => new LinPedidoVta
        {
            Empresa = EMPRESA, Número = PEDIDO, Nº_Orden = orden, Nº_Factura = ABONO, Estado = Constantes.EstadosLineaVenta.FACTURA,
            TipoLinea = 1, Producto = producto, Texto = "PRODUCTO " + producto, Cantidad = cantidad, Precio = precio, Aplicar_Dto = true,
            Nº_Cliente = CLIENTE, Contacto = "0", IVA = "G21", Almacén = "ALG", Forma_Venta = "VAR"
        };

        /// <param name="seriePedido">La serie del pedido 931334 (RV en el caso real; NV para comprobar que lo de siempre no cambia).</param>
        private static Escenario CrearEscenarioRV2600189(string seriePedido = "RV", string facturaAbono = ABONO)
        {
            var e = new Escenario
            {
                Db = A.Fake<NVEntities>(),
                ServicioPedidos = A.Fake<IServicioPedidosVenta>(),
                ServicioAlbaranes = A.Fake<IServicioAlbaranesVenta>(),
                ServicioFacturas = A.Fake<IServicioFacturas>(),
                Pendientes = A.Fake<IAlmacenRectificativasPendientes>(),
                Lineas = new List<LinPedidoVta>
                {
                    LineaAbono(1, "P1", -1, 10M),
                    LineaAbono(2, "P2", -2, 5M),
                    LineaAbono(3, "P3", -1, 7M)
                }
            };
            foreach (LinPedidoVta linea in e.Lineas)
            {
                linea.Nº_Factura = facturaAbono;
            }
            A.CallTo(() => e.Db.LinPedidoVtas).Returns(SetVivo(e.Lineas));
            var pedido = new CabPedidoVta { Empresa = EMPRESA, Número = PEDIDO, Nº_Cliente = CLIENTE, Contacto = "0", Serie = seriePedido, IVA = "G21" };
            A.CallTo(() => e.Db.CabPedidoVtas).Returns(SetVivo(new List<CabPedidoVta> { pedido }));
            A.CallTo(() => e.Db.CabsFacturasVtas).Returns(SetVivo(new List<CabFacturaVta>()));
            A.CallTo(() => e.Db.LinFacturaVtaRectificaciones).Returns(SetVivo(e.Vinculaciones));
            A.CallTo(() => e.Db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => e.ServicioPedidos.LeerParametroIVA(A<string>._, A<string>._, A<string>._))
                .Returns(new ParametroIVA { C__IVA = 21, C__RE = 0 });
            A.CallTo(() => e.ServicioAlbaranes.CrearAlbaran(A<string>._, A<int>._, A<string>._, A<DateTime?>._)).Returns(Task.FromResult(555));
            // El SP de facturación: las líneas en curso del pedido pasan a la factura nueva
            A.CallTo(() => e.ServicioFacturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._))
                .ReturnsLazily((string emp, int numero, string u, string ua) =>
                {
                    foreach (LinPedidoVta linea in e.Lineas.Where(l => l.Número == numero && l.Estado == Constantes.EstadosLineaVenta.EN_CURSO))
                    {
                        linea.Nº_Factura = DESHACE;
                        linea.Estado = Constantes.EstadosLineaVenta.FACTURA;
                    }
                    return Task.FromResult(new NestoAPI.Models.Facturas.CrearFacturaResponseDTO { NumeroFactura = DESHACE });
                });
            return e;
        }

        private static CopiarFacturaRequest RequestDeshacerDentroDelPedido(bool crearAlbaranYFactura = true, string factura = ABONO) => new CopiarFacturaRequest
        {
            Empresa = EMPRESA,
            Cliente = CLIENTE,
            NumeroFactura = factura,
            InvertirCantidades = true,
            AnadirAPedidoOriginal = true,
            MantenerCondicionesOriginales = true,
            CrearAlbaranYFactura = crearAlbaranYFactura
        };

        [TestMethod]
        public async Task CopiarFactura_RV2600189_InvertirLaRVDentroDeSuPedidoRV_VinculaLasPositivasALaRVYLaEnvia()
        {
            Escenario e = CrearEscenarioRV2600189();

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestDeshacerDentroDelPedido(), "Alfredo");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            Assert.AreEqual(DESHACE, respuesta.NumeroFactura);
            List<LinPedidoVta> nuevas = e.Lineas.Where(l => l.Nº_Factura == DESHACE).OrderBy(l => l.Nº_Orden).ToList();
            CollectionAssert.AreEqual(new short?[] { 1, 2, 1 }, nuevas.Select(l => l.Cantidad).ToList());
            // La forma del script OneShot_20261124 (que Verifactu aceptó «Correcto»)
            Assert.AreEqual(3, e.Vinculaciones.Count, "Antes no se guardaba ninguna: solo se vinculaban las negativas");
            for (int i = 0; i < 3; i++)
            {
                LinFacturaVtaRectificacion v = e.Vinculaciones.OrderBy(x => x.NumeroLinea).ElementAt(i);
                Assert.AreEqual("1", v.Empresa);
                Assert.AreEqual(DESHACE, v.NumeroFactura);
                Assert.AreEqual(nuevas[i].Nº_Orden, v.NumeroLinea);
                Assert.AreEqual(ABONO, v.FacturaOriginalNumero);
                Assert.AreEqual(i + 1, v.FacturaOriginalLinea, "La línea de la RV2600186 del mismo producto");
                Assert.AreEqual(Math.Abs((decimal)nuevas[i].Cantidad), v.CantidadRectificada);
            }
            A.CallTo(() => e.ServicioFacturas.EnviarRectificativaAVerifactu(EMPRESA, DESHACE)).MustHaveHappenedOnceExactly();
            A.CallTo(() => e.Pendientes.GuardarPendientes(A<string>._, A<int>._, A<List<RectificativaPendienteDTO>>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task CopiarFactura_RV2600189_SinFacturarEnElActo_LasPositivasQuedanPendientesDeVincularALaRV()
        {
            Escenario e = CrearEscenarioRV2600189();
            List<RectificativaPendienteDTO> guardadas = null;
            A.CallTo(() => e.Pendientes.GuardarPendientes(EMPRESA, PEDIDO, A<List<RectificativaPendienteDTO>>._))
                .Invokes((string emp, int pedido, List<RectificativaPendienteDTO> p) => guardadas = p);

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestDeshacerDentroDelPedido(crearAlbaranYFactura: false), "Alfredo");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            A.CallTo(() => e.ServicioFacturas.CrearFactura(A<string>._, A<int>._, A<string>._, A<string>._)).MustNotHaveHappened();
            Assert.IsNotNull(guardadas, "Antes no se guardaba nada: al facturar a mano salía otra RV2600189");
            Assert.AreEqual(3, guardadas.Count);
            List<LinPedidoVta> nuevas = e.Lineas.Where(l => l.Estado == Constantes.EstadosLineaVenta.EN_CURSO).OrderBy(l => l.Nº_Orden).ToList();
            CollectionAssert.AreEqual(nuevas.Select(l => l.Nº_Orden).ToList(), guardadas.Select(p => p.NumeroLinea).ToList());
            Assert.IsTrue(guardadas.All(p => p.FacturaOriginalNumero == ABONO));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, guardadas.Select(p => p.FacturaOriginalLinea).ToList());
            CollectionAssert.AreEqual(new[] { 1M, 2M, 1M }, guardadas.Select(p => p.CantidadRectificada).ToList());
        }

        [TestMethod]
        public async Task CopiarFactura_AbonoDeSiempreDentroDeUnPedidoNV_SoloVinculaLasNegativas()
        {
            // Lo de siempre no cambia: una NV con sus líneas positivas, abonada dentro de su pedido, vincula solo las negativas.
            Escenario e = CrearEscenarioRV2600189(seriePedido: "NV", facturaAbono: "NV2618549");
            foreach (LinPedidoVta linea in e.Lineas)
            {
                linea.Cantidad = (short)-linea.Cantidad; // la venta, en positivo
            }

            CopiarFacturaResponse respuesta = await e.Gestor().CopiarFactura(RequestDeshacerDentroDelPedido(factura: "NV2618549"), "Alfredo");

            Assert.IsTrue(respuesta.Exitoso, respuesta.Mensaje);
            Assert.AreEqual(3, e.Vinculaciones.Count);
            Assert.IsTrue(e.Vinculaciones.All(v => v.FacturaOriginalNumero == "NV2618549"));
            List<int> negativas = e.Lineas.Where(l => l.Nº_Factura == DESHACE && l.Cantidad < 0).Select(l => l.Nº_Orden).ToList();
            CollectionAssert.AreEquivalent(negativas, e.Vinculaciones.Select(v => v.NumeroLinea).ToList());
            A.CallTo(() => e.ServicioFacturas.EnviarRectificativaAVerifactu(EMPRESA, DESHACE)).MustHaveHappenedOnceExactly();
        }

        #endregion

        #region Auto-curado del job (ServicioFacturas)

        private sealed class EscenarioJob
        {
            public NVEntities Db;
            public IServicioVerifactu Verifactu;
            public ILogService Log;
            public IAlmacenRectificativasPendientes Pendientes;
            public List<LinFacturaVtaRectificacion> Vinculaciones = new List<LinFacturaVtaRectificacion>();
            public VerifactuFacturaRequest Enviado;

            public ServicioFacturas Servicio() => new ServicioFacturas(Db, Verifactu, Log, Pendientes);
        }

        private static LinPedidoVta Linea(string factura, int pedido, int orden, string producto, short cantidad, decimal baseImponible) => new LinPedidoVta
        {
            Empresa = EMPRESA, Número = pedido, Nº_Orden = orden, Nº_Factura = factura, Estado = Constantes.EstadosLineaVenta.FACTURA,
            Producto = producto, Cantidad = cantidad, Nº_Cliente = CLIENTE, PorcentajeIVA = 21, PorcentajeRE = 0,
            Base_Imponible = baseImponible, ImporteIVA = baseImponible * 0.21M
        };

        private static EscenarioJob CrearEscenarioJob(List<LinPedidoVta> lineasDeshace, List<LinPedidoVta> otrasLineas, params CabFacturaVta[] otrasFacturas)
            => CrearEscenarioJob(EMPRESA, DESHACE, lineasDeshace, otrasLineas, otrasFacturas);

        private static EscenarioJob CrearEscenarioJob(string empresaFactura, string numeroFactura, List<LinPedidoVta> lineasDeshace,
            List<LinPedidoVta> otrasLineas, params CabFacturaVta[] otrasFacturas)
        {
            var e = new EscenarioJob
            {
                Db = A.Fake<NVEntities>(),
                Verifactu = A.Fake<IServicioVerifactu>(),
                Log = A.Fake<ILogService>(),
                Pendientes = A.Fake<IAlmacenRectificativasPendientes>()
            };
            var factura = new CabFacturaVta
            {
                Empresa = empresaFactura,
                Serie = "RV",
                Número = numeroFactura,
                Nº_Cliente = CLIENTE,
                Fecha = DateTime.Today,
                CifNif = "12345678Z",
                NombreFiscal = "CLIENTE DE PRUEBA SL",
                LinPedidoVtas = lineasDeshace
            };
            var facturas = new List<CabFacturaVta> { factura };
            facturas.AddRange(otrasFacturas);
            DbSet<CabFacturaVta> setFacturas = SetVivo(facturas);
            A.CallTo(() => setFacturas.Include(A<string>._)).Returns(setFacturas);
            A.CallTo(() => setFacturas.AsNoTracking()).Returns(setFacturas);
            A.CallTo(() => e.Db.CabsFacturasVtas).Returns(setFacturas);
            var todas = new List<LinPedidoVta>(lineasDeshace);
            todas.AddRange(otrasLineas);
            A.CallTo(() => e.Db.LinPedidoVtas).Returns(SetVivo(todas));
            A.CallTo(() => e.Db.LinFacturaVtaRectificaciones).Returns(SetVivo(e.Vinculaciones));
            A.CallTo(() => e.Db.ParametrosIVA).Returns(SetVivo(new List<ParametroIVA>()));
            A.CallTo(() => e.Db.VerifactuRegistros).Returns(SetVivo(new List<VerifactuRegistro>()));
            A.CallTo(() => e.Pendientes.LeerPorFacturasOriginales(A<string>._, A<IList<string>>._))
                .Returns(Task.FromResult(new List<RectificativaPendienteDTO>()));
            A.CallTo(() => e.Verifactu.EstaHabilitado).Returns(true);
            A.CallTo(() => e.Verifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>._))
                .Invokes((VerifactuFacturaRequest r) => e.Enviado = r)
                .Returns(new VerifactuResponse { Exitoso = true, Uuid = "uuid-892", Estado = "Correcto" });
            return e;
        }

        private static CabFacturaVta Cabecera(string numero, string serie, DateTime fecha)
            => new CabFacturaVta { Empresa = EMPRESA, Serie = serie, Número = numero, Fecha = fecha, LinPedidoVtas = new List<LinPedidoVta>() };

        [TestMethod]
        public async Task Job_RV2600189SinVinculaciones_SeAutocuraConLaRV2600186YSeDeclaraComoSuRectificativa()
        {
            DateTime ayer = DateTime.Today.AddDays(-1);
            EscenarioJob e = CrearEscenarioJob(
                new List<LinPedidoVta>
                {
                    Linea(DESHACE, PEDIDO, 4, "P1", 1, 10M),
                    Linea(DESHACE, PEDIDO, 5, "P2", 2, 10M),
                    Linea(DESHACE, PEDIDO, 6, "P3", 1, 7M)
                },
                new List<LinPedidoVta>
                {
                    Linea(ABONO, PEDIDO, 1, "P1", -1, -10M),
                    Linea(ABONO, PEDIDO, 2, "P2", -2, -10M),
                    Linea(ABONO, PEDIDO, 3, "P3", -1, -7M),
                    // La venta original: positiva, no es candidata
                    Linea("NV2618549", 931000, 1, "P1", 1, 10M)
                },
                Cabecera(ABONO, "RV", ayer),
                Cabecera("NV2618549", "NV", ayer.AddDays(-5)));

            VerifactuResponse respuesta = await e.Servicio().EnviarRectificativaAVerifactu(EMPRESA, DESHACE);

            Assert.IsNotNull(respuesta, "Antes se quedaba sin declarar: «es una devolución y no encontramos la factura…»");
            Assert.IsTrue(respuesta.Exitoso);
            Assert.AreEqual(3, e.Vinculaciones.Count);
            Assert.IsTrue(e.Vinculaciones.All(v => v.Empresa == "1" && v.NumeroFactura == DESHACE && v.FacturaOriginalNumero == ABONO),
                "Empresa char(1) y número sin relleno");
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, e.Vinculaciones.OrderBy(v => v.NumeroLinea).Select(v => v.FacturaOriginalLinea).ToList());
            Assert.IsNotNull(e.Enviado);
            Assert.AreEqual("I", e.Enviado.TipoRectificacion, "Rectificativa por diferencias");
            Assert.AreEqual("RV", e.Enviado.FacturasRectificadas.Single().Serie);
            Assert.AreEqual("2600186", e.Enviado.FacturasRectificadas.Single().Numero);
            Assert.IsTrue(e.Enviado.ImporteTotal > 0, "Los importes en positivo: deshace el abono");
        }

        [TestMethod]
        public async Task GenerarVinculacionesRectificativaDeRectificativa_EmpresaYNumeroConRellenoDeLaBd_LosNormaliza()
        {
            // RV2600001: CabFacturaVta.Empresa es char(3) («1  ») y LinFacturaVtaRectificacion.Empresa char(1)
            EscenarioJob e = CrearEscenarioJob("1  ", DESHACE + " ",
                new List<LinPedidoVta> { Linea(DESHACE, PEDIDO, 4, "P1", 1, 10M) },
                new List<LinPedidoVta> { Linea(ABONO, PEDIDO, 1, "P1", -1, -10M) },
                Cabecera(ABONO, "RV", DateTime.Today.AddDays(-1)));
            CabFacturaVta factura = e.Db.CabsFacturasVtas.ToList().First(f => f.Número.Trim() == DESHACE);

            bool creadas = await e.Servicio().GenerarVinculacionesRectificativaDeRectificativa(factura);

            Assert.IsTrue(creadas);
            Assert.AreEqual("1", e.Vinculaciones.Single().Empresa);
            Assert.AreEqual(DESHACE, e.Vinculaciones.Single().NumeroFactura);
            Assert.AreEqual(ABONO, e.Vinculaciones.Single().FacturaOriginalNumero);
        }

        [TestMethod]
        public async Task Job_RectificativaEnPositivoSinAbonoQueCase_NoSeCuraNiSeEnvia()
        {
            // Hay una RV del cliente con P1, pero en la cantidad no casa: mejor no curar que curar mal.
            EscenarioJob e = CrearEscenarioJob(
                new List<LinPedidoVta> { Linea(DESHACE, PEDIDO, 4, "P1", 2, 20M) },
                new List<LinPedidoVta> { Linea(ABONO, PEDIDO, 1, "P1", -1, -10M) },
                Cabecera(ABONO, "RV", DateTime.Today.AddDays(-1)));

            VerifactuResponse respuesta = await e.Servicio().EnviarRectificativaAVerifactu("1", DESHACE);

            Assert.IsNull(respuesta);
            Assert.AreEqual(0, e.Vinculaciones.Count);
            A.CallTo(() => e.Verifactu.EnviarFacturaAsync(A<VerifactuFacturaRequest>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_LaRectificativaAnteriorYaEstaCorregida_NoSeVinculaOtraVez()
        {
            var yaDeshecha = new LinFacturaVtaRectificacion { Empresa = EMPRESA, NumeroFactura = "RV2600188", NumeroLinea = 9, FacturaOriginalNumero = ABONO, FacturaOriginalLinea = 1, CantidadRectificada = 1 };
            EscenarioJob e = CrearEscenarioJob(
                new List<LinPedidoVta> { Linea(DESHACE, PEDIDO, 4, "P1", 1, 10M) },
                new List<LinPedidoVta> { Linea(ABONO, PEDIDO, 1, "P1", -1, -10M) },
                Cabecera(ABONO, "RV", DateTime.Today.AddDays(-1)));
            e.Vinculaciones.Add(yaDeshecha);

            VerifactuResponse respuesta = await e.Servicio().EnviarRectificativaAVerifactu("1", DESHACE);

            Assert.IsNull(respuesta);
            Assert.AreEqual(1, e.Vinculaciones.Count, "Solo la que ya había");
        }

        [TestMethod]
        public async Task Job_UnaVentaConLineaNegativaNoEsCandidata_SoloLasRectificativas()
        {
            // El cliente tiene una NV con una línea negativa de P1 (factura mixta): no es un abono que deshacer.
            EscenarioJob e = CrearEscenarioJob(
                new List<LinPedidoVta> { Linea(DESHACE, PEDIDO, 4, "P1", 1, 10M) },
                new List<LinPedidoVta> { Linea("NV2618000", 930000, 3, "P1", -1, -10M) },
                Cabecera("NV2618000", "NV", DateTime.Today.AddDays(-3)));

            VerifactuResponse respuesta = await e.Servicio().EnviarRectificativaAVerifactu("1", DESHACE);

            Assert.IsNull(respuesta);
            Assert.AreEqual(0, e.Vinculaciones.Count);
        }

        #endregion
    }
}
