using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Cupones;
using NestoAPI.Infraestructure.Eventos;
using NestoAPI.Infraestructure.Exceptions;
using NestoAPI.Models;
using NestoAPI.Models.Picking;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace NestoAPI.Tests.Infrastructure.Cupones
{
    /// <summary>
    /// NestoAPI#613 (Carlos, 29/10/26, a petición del director comercial): el pedido mínimo para usar el cupón de un evento lo
    /// define el evento (0 por defecto y en los ya creados) y se guarda en el cupón al emitirlo. Los cupones sin mínimo guardado
    /// (de antes del script) siguen con el doble. Con 0, los textos no hablan de pedido mínimo.
    /// </summary>
    [TestClass]
    public class CuponPedidoMinimoPorEventoTests
    {
        private const string CLIENTE = "15191";
        private const int PEDIDO = 930500;
        private const string PRODUCTO = "CUPONEVENTO";
        private static readonly DateTime HOY = new DateTime(2026, 11, 2, 10, 0, 0);
        private static readonly DateTime FECHA_EVENTO = new DateTime(2026, 10, 30);

        private static CuponLeido Cupon(decimal? pedidoMinimo, decimal importe = 100)
        {
            CuponLeido c = CuponesTests.Cupon();
            c.Importe = importe;
            c.PedidoMinimo = pedidoMinimo;
            return c;
        }

        // ---------------- La regla (un solo sitio) ----------------

        [TestMethod]
        public void PedidoMinimo_ElGuardadoManda_YSinGuardarElDobleDeAntes()
        {
            Assert.AreEqual(0M, ReglasCupon.PedidoMinimo(Cupon(0)), "Evento sin mínimo: cualquier importe");
            Assert.AreEqual(200M, ReglasCupon.PedidoMinimo(Cupon(200)), "El del evento, no el doble del cupón");
            Assert.AreEqual(200M, ReglasCupon.PedidoMinimo(Cupon(null)), "Cupón de antes del script: el doble (100 € → 200 €)");
            Assert.AreEqual(150M, ReglasCupon.PedidoMinimo(Cupon(150, importe: 50)), "Aunque no sea el doble");
        }

        // ---------------- Emisión ----------------

        private static async Task<CuponNuevo> Emitir(decimal? pedidoMinimoEvento)
        {
            IRepositorioCupones repositorio = A.Fake<IRepositorioCupones>();
            CuponNuevo insertado = null;
            A.CallTo(() => repositorio.LeerDeInscripcion(101)).Returns(Task.FromResult<CuponLeido>(null));
            A.CallTo(() => repositorio.LeerPedidoMinimoCuponEvento(1)).Returns(Task.FromResult(pedidoMinimoEvento));
            A.CallTo(() => repositorio.Insertar(A<CuponNuevo>._, A<Func<string>>._)).ReturnsLazily((CuponNuevo c, Func<string> f) =>
            {
                insertado = c;
                return Task.FromResult(CuponesTests.Cupon());
            });
            var emisor = new EmisorCuponesEvento(repositorio, A.Fake<IAvisadorCupones>(), () => HOY);

            await emisor.Emitir(new EventoInscripcion { Id = 101, Empresa = "1", Cliente = CLIENTE, Estado = "Asistio" },
                new Evento { Id = 1, Fecha = FECHA_EVENTO, ImporteSenal = 100 }, null, "x");

            Assert.IsNotNull(insertado);
            return insertado;
        }

        [TestMethod]
        public async Task Emitir_EventoSinMinimo_ElCuponLlevaCero()
        {
            Assert.AreEqual(0M, (await Emitir(0)).PedidoMinimo);
        }

        [TestMethod]
        public async Task Emitir_EventoSinLaColumnaOSinInformar_ElCuponLlevaCero()
        {
            Assert.AreEqual(0M, (await Emitir(null)).PedidoMinimo, "NULL en el evento = 0");
        }

        [TestMethod]
        public async Task Emitir_EventoCon200_ElCuponLleva200()
        {
            CuponNuevo cupon = await Emitir(200);
            Assert.AreEqual(200M, cupon.PedidoMinimo);
            Assert.AreEqual(100M, cupon.Importe, "El importe sigue siendo la señal");
        }

        // ---------------- Uso en el pedido ----------------

        [TestMethod]
        public void Usar_ConMinimoCero_CualquierImporteVale()
        {
            ReglasCupon.Comprobar(Cupon(0), "1", PEDIDO, CLIENTE, HOY, false, 0M);
            ReglasCupon.Comprobar(Cupon(0), "1", PEDIDO, CLIENTE, HOY, false, 12.5M);
        }

        [TestMethod]
        public void Usar_ConMinimo200_PideLos200()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglasCupon.Comprobar(Cupon(200, importe: 50), "1", PEDIDO, CLIENTE, HOY, false, 199.99M));
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "al menos 200,00 € de productos");
            StringAssert.Contains(ex.Message, "faltan 0,01 €");
            ReglasCupon.Comprobar(Cupon(200, importe: 50), "1", PEDIDO, CLIENTE, HOY, false, 200M);
        }

        [TestMethod]
        public void Usar_CuponDeAntesSinMinimoGuardado_ElDoble()
        {
            var ex = Assert.ThrowsException<NestoBusinessException>(() => ReglasCupon.Comprobar(Cupon(null, importe: 50), "1", PEDIDO, CLIENTE, HOY, false, 99.99M));
            Assert.AreEqual(ReglasCupon.ERROR_MINIMO, ex.Context.ErrorCode);
            StringAssert.Contains(ex.Message, "al menos 100,00 €");
            ReglasCupon.Comprobar(Cupon(null, importe: 50), "1", PEDIDO, CLIENTE, HOY, false, 100M);
        }

        // ---------------- Textos ----------------

        [TestMethod]
        public void Textos_ConMinimoCero_NoHablanDePedidoMinimo()
        {
            CuponLeido cupon = Cupon(0);
            CuponDTO dto = ReglasCupon.ADTO(cupon, HOY, PRODUCTO);
            Assert.AreEqual(0M, dto.PedidoMinimo);
            Assert.AreEqual("Tiene un cupón de 100,00 € (EV-2610-7K3Q) para un pedido de productos, hasta el 30/04/2027.", dto.Mensaje);

            NotificacionPushDTO push = AvisadorCupones.Notificacion(cupon, "Luna");
            Assert.IsFalse(push.Cuerpo.Contains("al menos"), push.Cuerpo);
            StringAssert.EndsWith(push.Cuerpo, "en un pedido de productos.");

            using (MailMessage mail = AvisadorCupones.Correo(cupon, "Luna", "luna@example.com"))
            {
                Assert.IsFalse(mail.Body.Contains("al menos"), "Sin cifra de mínimo");
                StringAssert.Contains(mail.Body, "Se usa una sola vez, en un pedido de productos");
            }
        }

        [TestMethod]
        public void Textos_ConMinimo200_DicenLaCifra()
        {
            CuponLeido cupon = Cupon(200);
            CuponDTO dto = ReglasCupon.ADTO(cupon, HOY, PRODUCTO);
            Assert.AreEqual(200M, dto.PedidoMinimo);
            StringAssert.Contains(dto.Mensaje, "para un pedido de al menos 200,00 € de productos");
            StringAssert.Contains(AvisadorCupones.Notificacion(cupon, "Luna").Cuerpo, "de al menos 200,00 € de productos");
            using (MailMessage mail = AvisadorCupones.Correo(cupon, "Luna", "luna@example.com"))
            {
                StringAssert.Contains(mail.Body, "al menos 200,00 € de productos");
            }
        }

        // ---------------- Espejo de PrestaShop ----------------

        private static CuponEspejo Espejo(decimal? pedidoMinimo) => new CuponEspejo
        {
            Id = 7,
            Codigo = "EV-2610-7K3Q",
            Empresa = "1",
            Cliente = CLIENTE,
            Importe = 100,
            PedidoMinimo = pedidoMinimo,
            ValidoDesde = FECHA_EVENTO,
            Caducidad = ReglasCupon.Caducidad(FECHA_EVENTO),
            Estado = EstadosCupon.ACTIVO
        };

        private static string MinimoEnPrestashop(decimal? pedidoMinimo) =>
            XDocument.Parse(ReglasEspejoPrestashop.XmlCartRule(Espejo(pedidoMinimo), 5, true, null, 1, 1))
                .Root.Element("cart_rule").Element("minimum_amount").Value;

        [TestMethod]
        public void Espejo_MinimoDelCupon()
        {
            Assert.AreEqual("0.00", MinimoEnPrestashop(0), "Sin mínimo");
            Assert.AreEqual("200.00", MinimoEnPrestashop(200));
            Assert.AreEqual("200.00", MinimoEnPrestashop(null), "Cupón de antes: el doble");
        }

        // ---------------- Picking ----------------

        [TestMethod]
        public void Picking_ConMinimoCero_ElCuponSaleEnLaPrimeraEntrega()
        {
            var pedido = new PedidoPicking
            {
                Empresa = "1",
                Id = 1,
                Lineas = new List<LineaPedidoPicking>
                {
                    new LineaPedidoPicking { Id = 1, NumeroPedido = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = "CREMA", Cantidad = 1, Total = 20, BaseImponible = 16.53M, CantidadReservada = 1 },
                    new LineaPedidoPicking { Id = 2, NumeroPedido = 1, TipoLinea = Constantes.TiposLineaVenta.PRODUCTO, Producto = PRODUCTO, Cantidad = -1, Total = -100, BaseImponible = -82.64M, CantidadReservada = 0 }
                }
            };
            var productos = new Dictionary<string, ProductoParaCupon>(StringComparer.OrdinalIgnoreCase)
            {
                [PRODUCTO] = new ProductoParaCupon { Numero = PRODUCTO, Grupo = "COS", Ficticio = true },
                ["CREMA"] = new ProductoParaCupon { Numero = "CREMA", Grupo = "COS" }
            };

            CuponesPicking.Rellenar(new List<PedidoPicking> { pedido }, PRODUCTO, (e, c) => productos, p => new List<LineaEntregadaCupon>(),
                p => new CuponUsadoPicking { Importe = 100, PedidoMinimo = 0 });
            GestorReservasStock.ReservarCupones(new List<PedidoPicking> { pedido });

            Assert.AreEqual(0M, pedido.Cupon.PedidoMinimo);
            Assert.IsFalse(pedido.Cupon.Retenido, "Sin mínimo, el cupón no se retiene");
            Assert.IsTrue(pedido.Lineas.Any(l => l.EsCupon));
        }

        // ---------------- El evento (mantenimiento) ----------------

        private NVEntities db;
        private IRepositorioPedidoMinimoCuponEventos repositorioEventos;

        private ServicioEventos Servicio(bool conRepositorio = true)
        {
            db = A.Fake<NVEntities>();
            var eventos = new List<Evento>
            {
                new Evento { Id = 1, Empresa = "1", Titulo = "Masterclass Cloasma", Fecha = new DateTime(2026, 11, 20), ImporteSenal = 100, ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "ALC", Usuario = "x", FechaModificacion = HOY },
                new Evento { Id = 5, Empresa = "1", Titulo = "Curso Peeling", Fecha = new DateTime(2026, 11, 26), ImporteSenal = 100, ImporteSenalNoReembolsable = 60, Activo = true, Almacen = "ALC", Usuario = "x", FechaModificacion = HOY }
            };
            A.CallTo(() => db.Eventos).Returns(Crear(eventos));
            A.CallTo(() => db.EventosInscripciones).Returns(Crear(new List<EventoInscripcion>()));
            A.CallTo(() => db.EventosSenales).Returns(Crear(new List<EventoSenal>()));
            A.CallTo(() => db.Productos).Returns(Crear(new List<Producto>()));
            repositorioEventos = A.Fake<IRepositorioPedidoMinimoCuponEventos>();
            A.CallTo(() => repositorioEventos.Leer(A<IReadOnlyCollection<int>>._))
                .Returns(Task.FromResult(new Dictionary<int, decimal> { [5] = 200 }));
            A.CallTo(() => repositorioEventos.Guardar(A<int>._, A<decimal>._)).Returns(Task.FromResult(true));
            var servicio = new ServicioEventos(db, () => HOY, (e, u) => "ALC");
            servicio.PedidoMinimoCupon = conRepositorio ? repositorioEventos : null;
            return servicio;
        }

        private static EventoDTO Put(decimal? pedidoMinimo) => new EventoDTO
        {
            Titulo = "Curso Peeling",
            Fecha = new DateTime(2026, 11, 26),
            Activo = true,
            PedidoMinimoCupon = pedidoMinimo
        };

        [TestMethod]
        public async Task LeerEventos_ElMinimoDeCadaEvento_CeroSiNoLoTiene()
        {
            List<EventoDTO> lista = await Servicio().LeerEventos("1", true);

            Assert.AreEqual(0M, lista.Single(e => e.Id == 1).PedidoMinimoCupon, "Siempre un importe: 0 por defecto");
            Assert.AreEqual(200M, lista.Single(e => e.Id == 5).PedidoMinimoCupon);
        }

        [TestMethod]
        public async Task LeerEventos_SinElScript_Cero()
        {
            List<EventoDTO> lista = await Servicio(conRepositorio: false).LeerEventos("1", true);

            Assert.IsTrue(lista.All(e => e.PedidoMinimoCupon == 0));
        }

        [TestMethod]
        public async Task PutEvento_Con200_LoGuarda()
        {
            ServicioEventos servicio = Servicio();

            EventoDTO dto = await servicio.ModificarEvento(5, Put(200), "x");

            A.CallTo(() => repositorioEventos.Guardar(5, 200M)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(200M, dto.PedidoMinimoCupon);
        }

        [TestMethod]
        public async Task PostEvento_Con200_LoGuarda()
        {
            ServicioEventos servicio = Servicio();
            DbSet<Evento> eventos = Crear(new List<Evento>());
            A.CallTo(() => eventos.Add(A<Evento>._)).ReturnsLazily((Evento e) => { e.Id = 9; return e; });
            A.CallTo(() => db.Eventos).Returns(eventos);
            EventoDTO nuevo = Put(200);
            nuevo.Almacen = "ALC";

            _ = await servicio.CrearEvento(nuevo, "x");

            A.CallTo(() => repositorioEventos.Guardar(9, 200M)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task PutEvento_SinElCampo_NoLoToca()
        {
            ServicioEventos servicio = Servicio();

            _ = await servicio.ModificarEvento(5, Put(null), "x");

            A.CallTo(() => repositorioEventos.Guardar(A<int>._, A<decimal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutEvento_Negativo_400SinGuardar()
        {
            ServicioEventos servicio = Servicio();

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarEvento(5, Put(-1), "x"));

            StringAssert.Contains(ex.Message, "no puede ser negativo");
            A.CallTo(() => repositorioEventos.Guardar(A<int>._, A<decimal>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutEvento_SinLaColumna_200DiceQueFaltaElScript_Y0NoMolesta()
        {
            ServicioEventos servicio = Servicio();
            A.CallTo(() => repositorioEventos.Guardar(A<int>._, A<decimal>._)).Returns(Task.FromResult(false));

            var ex = await Assert.ThrowsExceptionAsync<NestoBusinessException>(() => servicio.ModificarEvento(5, Put(200), "x"));
            Assert.AreEqual(ServicioEventos.MENSAJE_FALTA_SCRIPT_PEDIDO_MINIMO, ex.Message);

            _ = await servicio.ModificarEvento(5, Put(0), "x");
        }

        private static DbSet<T> Crear<T>(List<T> datos) where T : class
        {
            DbSet<T> fake = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fake).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.ToList().GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fake).Provider)
                .ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)fake).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)fake).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)fake).GetEnumerator()).ReturnsLazily(() => datos.ToList().GetEnumerator());
            return fake;
        }
    }
}
