using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Modos = NestoAPI.Models.Constantes.Pedidos.ModosServicio;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#542 (corte 2): la nota de entrega con lo pendiente de un albarán facturado entero se crea sola.
    /// Caso real: pedido 926346 (Jesús, 16/09/26, todo junto), 12 líneas con Recoger, albarán 729667 del 22/09;
    /// Alfredo creó a mano las notas 926777 y 926969.
    /// </summary>
    [TestClass]
    public class CreadorNotaEntregaPendienteTests
    {
        private const int ALBARAN = 729667;
        private NVEntities db;
        private List<CabPedidoVta> cabeceras;
        private List<LinPedidoVta> lineas;
        private List<CabPedidoVta> cabecerasAnadidas;
        private List<LinPedidoVta> lineasAnadidas;
        private List<string> registrado;
        private CreadorNotaEntregaPendiente creador;

        [TestInitialize]
        public void Setup()
        {
            cabeceras = new List<CabPedidoVta>();
            lineas = new List<LinPedidoVta>();
            cabecerasAnadidas = new List<CabPedidoVta>();
            lineasAnadidas = new List<LinPedidoVta>();
            registrado = new List<string>();
            db = A.Fake<NVEntities>();
            DbSet<CabPedidoVta> fakeCabs = DbSetCon(cabeceras);
            DbSet<LinPedidoVta> fakeLineas = DbSetCon(lineas);
            A.CallTo(() => fakeCabs.Add(A<CabPedidoVta>._)).Invokes((CabPedidoVta c) => cabecerasAnadidas.Add(c)).ReturnsLazily((CabPedidoVta c) => c);
            A.CallTo(() => fakeLineas.Add(A<LinPedidoVta>._)).Invokes((LinPedidoVta l) => lineasAnadidas.Add(l)).ReturnsLazily((LinPedidoVta l) => l);
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeCabs);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.TomarSiguienteNumeroPedido()).Returns(926777);
            A.CallTo(() => db.SaveChangesAsync()).Returns(Task.FromResult(1));
            A.CallTo(() => db.SaveChangesAsync(A<CancellationToken>._)).Returns(Task.FromResult(1));

            IServicioPedidosVenta servicioPedidos = A.Fake<IServicioPedidosVenta>();
            A.CallTo(() => servicioPedidos.LeerParametroIVA(A<string>._, A<string>._, A<string>._))
                .Returns(new ParametroIVA { C__IVA = 21, C__RE = 0 });
            creador = new CreadorNotaEntregaPendiente(db, new GestorPedidosVenta(servicioPedidos), (ex, usuario) => registrado.Add(ex.Message));
        }

        private static DbSet<T> DbSetCon<T>(List<T> datos) where T : class
        {
            var fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            return fake;
        }

        private CabPedidoVta Original926346(byte? modoServicio = Modos.TODO_JUNTO, bool servirJunto = true)
        {
            var cab = new CabPedidoVta
            {
                Empresa = "1", Número = 926346, Nº_Cliente = "41223", Contacto = "2", Fecha = new DateTime(2026, 9, 16),
                Forma_Pago = "TRN", PlazosPago = "CONTADO", Primer_Vencimiento = new DateTime(2026, 9, 22), IVA = "G",
                Vendedor = "JE", Periodo_Facturacion = "NRM", Ruta = "FW", Serie = "NV", Origen = "1", ContactoCobro = "0",
                ServirJunto = servirJunto, ModoServicio = modoServicio, MantenerJunto = false, ModoFacturacion = 3,
                Comentarios = "Horario de 10 a 14", ComentarioPicking = "Frágil", Usuario = "NUEVAVISION\\jesus",
                AvisarConImporteAlCogerPicking = true, SuPedido = "PO-77"
            };
            cabeceras.Add(cab);
            return cab;
        }

        private LinPedidoVta LineaFacturada(int orden, string producto, short cantidad, int recoger, decimal precio, int? albaran = ALBARAN, bool yaFacturado = true)
        {
            var linea = new LinPedidoVta
            {
                Empresa = "1", Número = 926346, Nº_Orden = orden, Nº_Cliente = "41223", Contacto = "2",
                TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = producto, Texto = "Producto " + producto,
                Almacén = "ALG", Cantidad = cantidad, Recoger = recoger, Precio = precio, PrecioTarifa = precio, IVA = "G",
                Descuento = 0, DescuentoCliente = 0.1M, DescuentoProducto = 0, DescuentoPP = 0, Aplicar_Dto = true,
                Estado = Constantes.EstadosLineaVenta.FACTURA, YaFacturado = yaFacturado, Picking = 99615,
                Nº_Albarán = albaran, Fecha_Albarán = new DateTime(2026, 9, 22), Nº_Factura = "NV2615541", Fecha_Factura = new DateTime(2026, 9, 22),
                Fecha_Entrega = new DateTime(2026, 9, 16), Fecha_Modificación = new DateTime(2026, 9, 16, 11, 40, 45),
                Grupo = "COS", SubGrupo = "CAB", Familia = "FAM", Delegación = "MAD", Forma_Venta = "VAR", Usuario = "NUEVAVISION\\jesus", VtoBueno = true
            };
            lineas.Add(linea);
            return linea;
        }

        [TestMethod]
        public void InterpretarModo_SombraUnoYElResto()
        {
            Assert.AreEqual(ModoNotaEntregaAutomatica.Sombra, CreadorNotaEntregaPendiente.InterpretarModo("sombra"));
            Assert.AreEqual(ModoNotaEntregaAutomatica.Encendido, CreadorNotaEntregaPendiente.InterpretarModo("1 "));
            Assert.AreEqual(ModoNotaEntregaAutomatica.Apagado, CreadorNotaEntregaPendiente.InterpretarModo("0"));
            Assert.AreEqual(ModoNotaEntregaAutomatica.Apagado, CreadorNotaEntregaPendiente.InterpretarModo(null));
        }

        [TestMethod]
        public void LeerModo_SiFallaElParametro_Apagado()
        {
            var lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("sin BD"));

            Assert.AreEqual(ModoNotaEntregaAutomatica.Apagado, CreadorNotaEntregaPendiente.LeerModo(lector));
        }

        [TestMethod]
        public void LineasPendientes_SoloLasDeEseAlbaranConRecogerYaFacturadas()
        {
            LinPedidoVta pendiente = LineaFacturada(1, "38932", 1, recoger: 1, precio: 134);
            LineaFacturada(2, "40627", 2, recoger: 0, precio: 44);              // entregada entera
            LineaFacturada(3, "42860", 20, recoger: 20, precio: 3.9M, albaran: 729000); // de otro albarán del mismo pedido
            LineaFacturada(4, "28991", 10, recoger: 10, precio: 3.6M, yaFacturado: false); // aún sin albarán

            List<LinPedidoVta> resultado = CreadorNotaEntregaPendiente.LineasPendientes(lineas, ALBARAN);

            Assert.AreEqual(1, resultado.Count);
            Assert.AreSame(pendiente, resultado[0]);
        }

        [TestMethod]
        public void ConstruirCabecera_EsLaDelPedidoOriginal_ComoNotaDeEntregaEnlazada()
        {
            CabPedidoVta original = Original926346();

            CabPedidoVta nota = CreadorNotaEntregaPendiente.ConstruirCabecera(original, 926777, ALBARAN, "NUEVAVISION\\Alfredo", new DateTime(2026, 9, 22));

            Assert.AreEqual(926777, nota.Número);
            Assert.IsTrue(nota.NotaEntrega);
            Assert.AreEqual(926346, nota.PedidoOrigen);
            Assert.AreEqual(ALBARAN, nota.AlbaranOrigen);
            Assert.AreEqual(new DateTime(2026, 9, 22), nota.Fecha);
            // Carlos: ruta, forma de pago, plazos y vendedor del pedido original, no de la ficha
            Assert.AreEqual("FW", nota.Ruta);
            Assert.AreEqual("TRN", nota.Forma_Pago);
            Assert.AreEqual("CONTADO", nota.PlazosPago);
            Assert.AreEqual("JE", nota.Vendedor);
            Assert.AreEqual("41223", nota.Nº_Cliente);
            Assert.AreEqual("2", nota.Contacto);
            Assert.AreEqual("Frágil", nota.ComentarioPicking);
            Assert.AreEqual("PO-77", nota.SuPedido);
            Assert.AreEqual("Horario de 10 a 14\r\nNOTA DE ENTREGA: pendiente de entregar del pedido 926346 (albarán 729667)", nota.Comentarios);
            Assert.AreEqual("NUEVAVISION\\Alfredo", nota.Usuario);
            Assert.IsFalse(nota.MantenerJunto);
            Assert.IsNull(nota.ModoFacturacion, "Una nota no se factura");
            Assert.IsFalse(nota.AvisarConImporteAlCogerPicking, "No hay nada que cobrar al entregar");
        }

        [TestMethod]
        public void ConstruirCabecera_HeredaElModoDeServicio_YElCuatroPasaATodoJunto()
        {
            CabPedidoVta todoJunto = CreadorNotaEntregaPendiente.ConstruirCabecera(Original926346(Modos.TODO_JUNTO, true), 1, ALBARAN, "u", DateTime.Today);
            CabPedidoVta segunEntre = CreadorNotaEntregaPendiente.ConstruirCabecera(Original926346(Modos.SEGUN_VAYA_ENTRANDO, false), 2, ALBARAN, "u", DateTime.Today);
            CabPedidoVta trasReponer = CreadorNotaEntregaPendiente.ConstruirCabecera(Original926346(Modos.TRAS_REPONER_DE_TIENDAS, false), 3, ALBARAN, "u", DateTime.Today);
            CabPedidoVta modo4 = CreadorNotaEntregaPendiente.ConstruirCabecera(Original926346(Modos.AHORA_LO_QUE_HAY_Y_EL_RESTO_DE_UNA_VEZ, false), 4, ALBARAN, "u", DateTime.Today);
            CabPedidoVta sinModo = CreadorNotaEntregaPendiente.ConstruirCabecera(Original926346(null, true), 5, ALBARAN, "u", DateTime.Today);

            Assert.AreEqual(Modos.TODO_JUNTO, todoJunto.ModoServicio);
            Assert.IsTrue(todoJunto.ServirJunto);
            Assert.AreEqual(Modos.SEGUN_VAYA_ENTRANDO, segunEntre.ModoServicio);
            Assert.IsFalse(segunEntre.ServirJunto);
            Assert.AreEqual(Modos.TRAS_REPONER_DE_TIENDAS, trasReponer.ModoServicio);
            Assert.AreEqual(Modos.TODO_JUNTO, modo4.ModoServicio, "La nota ES el resto: va de una vez");
            Assert.IsTrue(modo4.ServirJunto);
            Assert.AreEqual(Modos.TODO_JUNTO, sinModo.ModoServicio, "Sin modo manda ServirJunto");
        }

        [TestMethod]
        public void ConstruirLinea_LasUnidadesARecoger_YaFacturadas_LimpiasDeAlbaranFacturaYPicking_ConImportesDeEsaCantidad()
        {
            CabPedidoVta nota = new CabPedidoVta { Empresa = "1", Número = 926777, IVA = "G" };
            LinPedidoVta original = LineaFacturada(1, "38709", 2, recoger: 2, precio: 315);
            original.Cantidad = 2;
            original.Recoger = 1; // se entregó 1 y queda 1

            LinPedidoVta linea = CreadorNotaEntregaPendiente.ConstruirLinea(original, nota, "NUEVAVISION\\Alfredo", new DateTime(2026, 9, 22));

            Assert.AreEqual(926777, linea.Número);
            Assert.AreEqual(0, linea.Nº_Orden, "Identidad: la pone la BD");
            Assert.AreEqual((short)1, linea.Cantidad);
            Assert.AreEqual(0, linea.Recoger);
            Assert.IsTrue(linea.YaFacturado);
            Assert.AreEqual(Constantes.EstadosLineaVenta.EN_CURSO, linea.Estado);
            Assert.AreEqual(0, linea.Picking);
            Assert.IsNull(linea.Nº_Albarán);
            Assert.IsNull(linea.Fecha_Albarán);
            Assert.IsNull(linea.Nº_Factura);
            Assert.IsNull(linea.Fecha_Factura);
            Assert.AreEqual(new DateTime(2026, 9, 22), linea.Fecha_Entrega);
            Assert.AreEqual(315, linea.Precio);
            Assert.AreEqual(0.1M, linea.DescuentoCliente);
            Assert.AreEqual("38709", linea.Producto);
            Assert.AreEqual("ALG", linea.Almacén);
            Assert.AreEqual("41223", linea.Nº_Cliente);
            Assert.AreEqual(new DateTime(2026, 9, 16, 11, 40, 45), linea.Fecha_Modificación, "Conserva la antigüedad: el cliente ya pagó");
            Assert.AreEqual("NUEVAVISION\\Alfredo", linea.Usuario);
            Assert.IsTrue(linea.VtoBueno);
        }

        [TestMethod]
        public async Task Crear_Encendido_CreaLaNotaConLasLineasPendientesYSusImportes()
        {
            Original926346();
            LineaFacturada(1, "38932", 1, recoger: 1, precio: 134);
            LineaFacturada(2, "42860", 20, recoger: 20, precio: 3.9M);
            LineaFacturada(3, "40627", 1, recoger: 0, precio: 44);

            CabPedidoVta nota = await creador.Crear("1", 926346, ALBARAN, "NUEVAVISION\\Alfredo", ModoNotaEntregaAutomatica.Encendido);

            Assert.IsNotNull(nota);
            Assert.AreEqual(926777, nota.Número);
            Assert.AreEqual(1, cabecerasAnadidas.Count);
            Assert.AreSame(nota, cabecerasAnadidas[0]);
            Assert.AreEqual(2, lineasAnadidas.Count);
            LinPedidoVta l1 = lineasAnadidas.Single(l => l.Producto == "38932");
            Assert.AreEqual((short)1, l1.Cantidad);
            Assert.AreEqual(134 * 0.9M, l1.Base_Imponible);
            Assert.AreEqual(21, l1.PorcentajeIVA);
            Assert.AreEqual(RoundingHelper.DosDecimalesRound(134 * 0.9M * 1.21M), RoundingHelper.DosDecimalesRound(l1.Total));
            LinPedidoVta l2 = lineasAnadidas.Single(l => l.Producto == "42860");
            Assert.AreEqual((short)20, l2.Cantidad);
            Assert.AreEqual(20 * 3.9M * 0.9M, l2.Base_Imponible);
            A.CallTo(() => db.SaveChangesAsync()).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, registrado.Count);
        }

        [TestMethod]
        public async Task Crear_Sombra_NoCreaNada_YRegistraLoQueHabriaCreado()
        {
            Original926346();
            LineaFacturada(1, "38932", 1, recoger: 1, precio: 134);

            CabPedidoVta nota = await creador.Crear("1", 926346, ALBARAN, "u", ModoNotaEntregaAutomatica.Sombra);

            Assert.IsNull(nota);
            Assert.AreEqual(0, cabecerasAnadidas.Count + lineasAnadidas.Count);
            Assert.AreEqual(1, registrado.Count);
            StringAssert.Contains(registrado[0], "SOMBRA");
            StringAssert.Contains(registrado[0], "926346");
            StringAssert.Contains(registrado[0], "38932 × 1");
        }

        [TestMethod]
        public async Task Crear_SinNadaPendiente_NoCreaNada()
        {
            Original926346();
            LineaFacturada(1, "40627", 1, recoger: 0, precio: 44);

            CabPedidoVta nota = await creador.Crear("1", 926346, ALBARAN, "u", ModoNotaEntregaAutomatica.Encendido);

            Assert.IsNull(nota);
            Assert.AreEqual(0, cabecerasAnadidas.Count);
        }

        [TestMethod]
        public async Task Crear_YaExisteLaNotaDeEseAlbaran_NoLaDuplica()
        {
            Original926346();
            LineaFacturada(1, "38932", 1, recoger: 1, precio: 134);
            cabeceras.Add(new CabPedidoVta { Empresa = "1", Número = 926777, NotaEntrega = true, PedidoOrigen = 926346, AlbaranOrigen = ALBARAN });

            CabPedidoVta nota = await creador.Crear("1", 926346, ALBARAN, "u", ModoNotaEntregaAutomatica.Encendido);

            Assert.IsNull(nota);
            Assert.AreEqual(0, cabecerasAnadidas.Count);
        }

        [TestMethod]
        public async Task Crear_OtroAlbaranDelMismoPedido_SiCreaOtraNota()
        {
            Original926346();
            LineaFacturada(1, "38932", 1, recoger: 1, precio: 134, albaran: 729700);
            cabeceras.Add(new CabPedidoVta { Empresa = "1", Número = 926777, NotaEntrega = true, PedidoOrigen = 926346, AlbaranOrigen = ALBARAN });

            CabPedidoVta nota = await creador.Crear("1", 926346, 729700, "u", ModoNotaEntregaAutomatica.Encendido);

            Assert.IsNotNull(nota);
            Assert.AreEqual(729700, nota.AlbaranOrigen);
        }

        [TestMethod]
        public async Task Crear_Apagado_NoMiraNada()
        {
            CabPedidoVta nota = await creador.Crear("1", 926346, ALBARAN, "u", ModoNotaEntregaAutomatica.Apagado);

            Assert.IsNull(nota);
            A.CallTo(() => db.CabPedidoVtas).MustNotHaveHappened();
        }
    }
}
