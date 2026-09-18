using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Sincronizacion
{
    /// <summary>
    /// NestoAPI#498: fechas de primer presupuesto, primer pedido y último pedido del cliente, que
    /// viajan a Odoo para mover los leads del CRM (odoo-custom-addons#8 y #9), y el job que decide
    /// a qué clientes hay que republicar.
    /// </summary>
    [TestClass]
    public class FechasComprasClientesTests
    {
        private const short PRESUPUESTO = Constantes.EstadosLineaVenta.PRESUPUESTO;
        private const short NOTA_ENTREGA = Constantes.EstadosLineaVenta.NOTA_ENTREGA;
        private const short PENDIENTE = Constantes.EstadosLineaVenta.PENDIENTE;
        private const short EN_CURSO = Constantes.EstadosLineaVenta.EN_CURSO;
        private const short ALBARAN = Constantes.EstadosLineaVenta.ALBARAN;
        private const short FACTURA = Constantes.EstadosLineaVenta.FACTURA;

        private static readonly DateTime AHORA = new DateTime(2026, 9, 18, 12, 0, 0);
        private static readonly DateTime DESDE = AHORA.AddMinutes(-FechasComprasClientesJobsService.MINUTOS_VENTANA_FRECUENTE);
        private static readonly DateTime TOCADO_AHORA = AHORA.AddMinutes(-10);
        private static readonly DateTime TOCADO_HACE_TIEMPO = new DateTime(2025, 1, 10);

        private NVEntities db;
        private DbSet<CabPedidoVta> fakeCabeceras;
        private DbSet<LinPedidoVta> fakeLineas;
        private List<CabPedidoVta> cabeceras;
        private int ultimoNumero;

        [TestInitialize]
        public void Inicializar()
        {
            db = A.Fake<NVEntities>();
            fakeCabeceras = A.Fake<DbSet<CabPedidoVta>>(o => o.Implements<IQueryable<CabPedidoVta>>().Implements<IDbAsyncEnumerable<CabPedidoVta>>());
            fakeLineas = A.Fake<DbSet<LinPedidoVta>>(o => o.Implements<IQueryable<LinPedidoVta>>().Implements<IDbAsyncEnumerable<LinPedidoVta>>());
            A.CallTo(() => db.CabPedidoVtas).Returns(fakeCabeceras);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            cabeceras = new List<CabPedidoVta>();
            ultimoNumero = 900000;
        }

        /// <summary>
        /// Un pedido con una línea por estado. Nº_Cliente va CON relleno, como llega de la BD
        /// (char(10)): es donde se rompen las comparaciones en memoria.
        /// La fecha de la LÍNEA (entrega) se pone distinta a propósito de la de la cabecera, para
        /// comprobar que se usa la de la cabecera.
        /// </summary>
        private CabPedidoVta Pedido(string cliente, DateTime fecha, DateTime? modificado = null,
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO, short[] estados = null)
        {
            var cabecera = new CabPedidoVta
            {
                Empresa = empresa,
                Número = ++ultimoNumero,
                Nº_Cliente = cliente.PadRight(10),
                Contacto = "0",
                Fecha = fecha,
                Fecha_Modificación = modificado ?? TOCADO_HACE_TIEMPO
            };
            short orden = 0;
            foreach (short estado in estados ?? new[] { PENDIENTE })
            {
                cabecera.LinPedidoVtas.Add(new LinPedidoVta
                {
                    Empresa = empresa,
                    Número = cabecera.Número,
                    Nº_Orden = ++orden,
                    Nº_Cliente = cabecera.Nº_Cliente,
                    Contacto = cabecera.Contacto,
                    Estado = estado,
                    Fecha_Entrega = fecha.AddDays(7),
                    Fecha_Modificación = cabecera.Fecha_Modificación,
                    CabPedidoVta = cabecera
                });
            }
            cabeceras.Add(cabecera);
            return cabecera;
        }

        private void Configurar()
        {
            ConfigurarFakeDbSet(fakeCabeceras, cabeceras.AsQueryable());
            ConfigurarFakeDbSet(fakeLineas, cabeceras.SelectMany(c => c.LinPedidoVtas).ToList().AsQueryable());
        }

        private async Task<FechasComprasCliente> Fechas(string cliente)
        {
            Configurar();
            return await CalculoFechasComprasCliente.Calcular(db, cliente);
        }

        private async Task<List<string>> ARepublicar(bool incluirUltimoPedido = false)
        {
            Configurar();
            return await FechasComprasClientesJobsService.ClientesARepublicar(db, DESDE, incluirUltimoPedido);
        }

        // ---------------------------------------------------------------------------------
        // Cálculo de las fechas
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public async Task Calcular_ClienteSinPedidos_LasTresFechasSonNull()
        {
            _ = Pedido("99999", new DateTime(2026, 1, 1));

            FechasComprasCliente fechas = await Fechas("15191");

            Assert.IsNull(fechas.FechaPrimerPresupuesto);
            Assert.IsNull(fechas.FechaPrimerPedido);
            Assert.IsNull(fechas.FechaUltimoPedido);
        }

        [TestMethod]
        public async Task Calcular_VariosPedidos_PrimeroYUltimoPorFechaDeCabecera()
        {
            _ = Pedido("15191", new DateTime(2025, 6, 1), estados: new[] { FACTURA });
            _ = Pedido("15191", new DateTime(2024, 3, 5), estados: new[] { ALBARAN });
            _ = Pedido("15191", new DateTime(2026, 9, 17), estados: new[] { PENDIENTE });

            FechasComprasCliente fechas = await Fechas("15191");

            Assert.AreEqual(new DateTime(2024, 3, 5), fechas.FechaPrimerPedido);
            Assert.AreEqual(new DateTime(2026, 9, 17), fechas.FechaUltimoPedido);
            Assert.IsNull(fechas.FechaPrimerPresupuesto);
        }

        [TestMethod]
        public async Task Calcular_Presupuesto_CuentaComoPresupuestoYNoComoPedido()
        {
            _ = Pedido("15191", new DateTime(2026, 2, 1), estados: new[] { PRESUPUESTO });
            _ = Pedido("15191", new DateTime(2026, 1, 1), estados: new[] { PRESUPUESTO });
            _ = Pedido("15191", new DateTime(2026, 5, 1), estados: new[] { EN_CURSO });

            FechasComprasCliente fechas = await Fechas("15191");

            Assert.AreEqual(new DateTime(2026, 1, 1), fechas.FechaPrimerPresupuesto);
            Assert.AreEqual(new DateTime(2026, 5, 1), fechas.FechaPrimerPedido);
            Assert.AreEqual(new DateTime(2026, 5, 1), fechas.FechaUltimoPedido);
        }

        // Decisión de Carlos (18/09/26): la nota de entrega no es un pedido.
        [TestMethod]
        public async Task Calcular_NotaDeEntrega_NoCuentaComoPedido()
        {
            _ = Pedido("15191", new DateTime(2026, 1, 1), estados: new[] { NOTA_ENTREGA });

            FechasComprasCliente fechas = await Fechas("15191");

            Assert.IsNull(fechas.FechaPrimerPedido);
            Assert.IsNull(fechas.FechaUltimoPedido);
            Assert.IsNull(fechas.FechaPrimerPresupuesto);
        }

        // Decisión de Carlos (18/09/26): la empresa espejo cuenta junto con la principal, así que
        // traspasar un pedido no cambia las fechas del cliente. Otras empresas no cuentan.
        [TestMethod]
        public async Task Calcular_EmpresaEspejoCuentaYOtraEmpresaNo()
        {
            _ = Pedido("15191", new DateTime(2023, 1, 1), empresa: "2", estados: new[] { FACTURA });
            _ = Pedido("15191", new DateTime(2024, 1, 1), empresa: Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO, estados: new[] { FACTURA });
            _ = Pedido("15191", new DateTime(2025, 1, 1), estados: new[] { FACTURA });

            FechasComprasCliente fechas = await Fechas("15191");

            Assert.AreEqual(new DateTime(2024, 1, 1), fechas.FechaPrimerPedido);
            Assert.AreEqual(new DateTime(2025, 1, 1), fechas.FechaUltimoPedido);
        }

        // Un pedido con líneas en estados mezclados (una servida, otra pendiente) es un pedido.
        [TestMethod]
        public async Task Calcular_PedidoConLineasEnVariosEstados_CuentaComoPedido()
        {
            _ = Pedido("15191", new DateTime(2026, 3, 3), estados: new[] { ALBARAN, PENDIENTE, NOTA_ENTREGA });

            FechasComprasCliente fechas = await Fechas("15191");

            Assert.AreEqual(new DateTime(2026, 3, 3), fechas.FechaPrimerPedido);
        }

        // La carga inicial: muchos clientes en una llamada, cada uno con lo suyo y las claves
        // recortadas (Nº_Cliente llega con relleno de la BD).
        [TestMethod]
        public async Task Calcular_VariosClientes_CadaUnoConSusFechasYClaveRecortada()
        {
            _ = Pedido("15191", new DateTime(2026, 1, 1));
            _ = Pedido("20000", new DateTime(2025, 1, 1), estados: new[] { PRESUPUESTO });
            Configurar();

            Dictionary<string, FechasComprasCliente> fechas = await CalculoFechasComprasCliente
                .Calcular(db, new[] { "15191     ", "20000", "30000" });

            CollectionAssert.AreEquivalent(new[] { "15191", "20000" }, fechas.Keys.ToList());
            Assert.AreEqual(new DateTime(2026, 1, 1), fechas["15191"].FechaPrimerPedido);
            Assert.AreEqual(new DateTime(2025, 1, 1), fechas["20000"].FechaPrimerPresupuesto);
            Assert.IsNull(CalculoFechasComprasCliente.Buscar(fechas, "30000").FechaPrimerPedido);
        }

        // ---------------------------------------------------------------------------------
        // El job: a quién hay que republicar
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public async Task ClientesARepublicar_PrimerPedidoDeUnClienteNuevo_SeEncola()
        {
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA);

            List<string> clientes = await ARepublicar();

            CollectionAssert.AreEqual(new List<string> { "15191" }, clientes);
        }

        // El caso que justifica filtrar: un pedido normal de un cliente de siempre no lo republica.
        [TestMethod]
        public async Task ClientesARepublicar_PedidoNuevoDeUnClienteConPedidosAnteriores_NoSeEncola()
        {
            _ = Pedido("15191", new DateTime(2020, 1, 1), estados: new[] { FACTURA });
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA);

            List<string> clientes = await ARepublicar();

            Assert.AreEqual(0, clientes.Count);
        }

        // La pasada nocturna sí lo republica, para refrescar FechaUltimoPedido.
        [TestMethod]
        public async Task ClientesARepublicar_PasadaNocturna_EncolaElUltimoPedido()
        {
            _ = Pedido("15191", new DateTime(2020, 1, 1), estados: new[] { FACTURA });
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA);

            List<string> clientes = await ARepublicar(incluirUltimoPedido: true);

            CollectionAssert.AreEqual(new List<string> { "15191" }, clientes);
        }

        // Tocar un pedido antiguo (facturar, un cambio) no cambia el último pedido: no se encola
        // ni siquiera de noche.
        [TestMethod]
        public async Task ClientesARepublicar_TocarUnPedidoAntiguo_NoSeEncolaNiDeNoche()
        {
            _ = Pedido("15191", new DateTime(2020, 1, 1), estados: new[] { FACTURA });
            _ = Pedido("15191", new DateTime(2026, 6, 1), TOCADO_AHORA, estados: new[] { FACTURA });
            _ = Pedido("15191", new DateTime(2026, 9, 1), estados: new[] { ALBARAN });

            List<string> clientes = await ARepublicar(incluirUltimoPedido: true);

            Assert.AreEqual(0, clientes.Count);
        }

        [TestMethod]
        public async Task ClientesARepublicar_PrimerPresupuestoDeUnClienteQueYaCompraba_SeEncola()
        {
            _ = Pedido("15191", new DateTime(2020, 1, 1), estados: new[] { FACTURA });
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA, estados: new[] { PRESUPUESTO });

            List<string> clientes = await ARepublicar();

            CollectionAssert.AreEqual(new List<string> { "15191" }, clientes);
        }

        [TestMethod]
        public async Task ClientesARepublicar_SegundoPresupuesto_NoSeEncola()
        {
            _ = Pedido("15191", new DateTime(2026, 1, 1), estados: new[] { PRESUPUESTO });
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA, estados: new[] { PRESUPUESTO });

            List<string> clientes = await ARepublicar();

            Assert.AreEqual(0, clientes.Count);
        }

        // Aceptar el primer presupuesto: la PUT toca la cabecera y las líneas pasan a pendiente,
        // así que es su primer pedido aunque la fecha de cabecera sea la de cuando se presupuestó.
        [TestMethod]
        public async Task ClientesARepublicar_AceptarElPresupuestoDeUnClienteSinPedidos_SeEncola()
        {
            _ = Pedido("15191", new DateTime(2026, 9, 1), TOCADO_AHORA, estados: new[] { PENDIENTE });

            List<string> clientes = await ARepublicar();

            CollectionAssert.AreEqual(new List<string> { "15191" }, clientes);
        }

        [TestMethod]
        public async Task ClientesARepublicar_PrimerPedidoTocadoFueraDeLaVentana_NoSeEncola()
        {
            _ = Pedido("15191", AHORA.Date, DESDE.AddMinutes(-1));

            List<string> clientes = await ARepublicar(incluirUltimoPedido: true);

            Assert.AreEqual(0, clientes.Count);
        }

        [TestMethod]
        public async Task ClientesARepublicar_SoloNotaDeEntrega_NoSeEncola()
        {
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA, estados: new[] { NOTA_ENTREGA });

            List<string> clientes = await ARepublicar(incluirUltimoPedido: true);

            Assert.AreEqual(0, clientes.Count);
        }

        [TestMethod]
        public async Task ClientesARepublicar_PrimerPedidoEnLaEmpresaEspejo_SeEncola()
        {
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA, empresa: Constantes.Empresas.EMPRESA_ESPEJO_POR_DEFECTO);

            List<string> clientes = await ARepublicar();

            CollectionAssert.AreEqual(new List<string> { "15191" }, clientes);
        }

        // Varios pedidos del mismo cliente nuevo en la ventana: se encola una vez.
        [TestMethod]
        public async Task ClientesARepublicar_VariosPedidosDelMismoClienteNuevo_SeEncolaUnaVez()
        {
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA, estados: new[] { PRESUPUESTO });
            _ = Pedido("15191", AHORA.Date, TOCADO_AHORA);
            _ = Pedido("20000", AHORA.Date, TOCADO_AHORA);

            List<string> clientes = await ARepublicar();

            CollectionAssert.AreEqual(new List<string> { "15191", "20000" }, clientes);
        }

        // ---------------------------------------------------------------------------------
        // El mensaje que llega a Odoo
        // ---------------------------------------------------------------------------------

        [TestMethod]
        public async Task PublicarClienteSincronizar_LlevaLasFechasDeComprasDelCliente()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerFechasCompras(A<IEnumerable<string>>._))
                .Returns(new Dictionary<string, FechasComprasCliente>
                {
                    ["15191"] = new FechasComprasCliente
                    {
                        FechaPrimerPresupuesto = new DateTime(2026, 1, 2),
                        FechaPrimerPedido = new DateTime(2026, 2, 3),
                        FechaUltimoPedido = new DateTime(2026, 9, 17)
                    }
                });
            ClienteSyncMessage mensaje = await PublicarYCapturar(servicio, "15191     ");

            Assert.AreEqual(new DateTime(2026, 1, 2), mensaje.FechaPrimerPresupuesto);
            Assert.AreEqual(new DateTime(2026, 2, 3), mensaje.FechaPrimerPedido);
            Assert.AreEqual(new DateTime(2026, 9, 17), mensaje.FechaUltimoPedido);
        }

        [TestMethod]
        public async Task PublicarClienteSincronizar_ClienteSinCompras_FechasNull()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => servicio.LeerFechasCompras(A<IEnumerable<string>>._))
                .Returns(new Dictionary<string, FechasComprasCliente>());

            ClienteSyncMessage mensaje = await PublicarYCapturar(servicio, "15191");

            Assert.IsNull(mensaje.FechaPrimerPresupuesto);
            Assert.IsNull(mensaje.FechaPrimerPedido);
            Assert.IsNull(mensaje.FechaUltimoPedido);
        }

        // Odoo vacía el campo si llega null (odoo-custom-addons#3): publicar sin las fechas
        // calculadas borraría las que ya tiene, así que no se publica nada.
        [TestMethod]
        public async Task PublicarClienteSincronizar_SinFechasCalculadas_NoPublica()
        {
            ISincronizacionEventPublisher publisher = A.Fake<ISincronizacionEventPublisher>();
            var gestor = new GestorClientes(A.Fake<IServicioGestorClientes>(), A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(publisher));

            await Assert.ThrowsExceptionAsync<ArgumentNullException>(() =>
                gestor.PublicarClienteSincronizar(new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0" },
                    (FechasComprasCliente)null, "Nesto viejo", "usuario"));

            A.CallTo(() => publisher.PublishEventAsync(A<string>._, A<object>._)).MustNotHaveHappened();
        }

        // Contrato con odoo-custom-addons#8: nombres en la raíz del mensaje, fecha ISO y null
        // explícito (Odoo vacía el campo si viene null y no lo toca si no viene).
        [TestMethod]
        public void ClienteSyncMessage_SerializaLasFechasConLosNombresQueEsperaOdoo()
        {
            var mensaje = new ClienteSyncMessage
            {
                Cliente = "15191",
                FechaPrimerPresupuesto = null,
                FechaPrimerPedido = new DateTime(2026, 2, 3),
                FechaUltimoPedido = new DateTime(2026, 9, 17)
            };

            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(mensaje)))
            {
                JsonElement raiz = json.RootElement;
                Assert.AreEqual(JsonValueKind.Null, raiz.GetProperty("FechaPrimerPresupuesto").ValueKind);
                Assert.AreEqual("2026-02-03T00:00:00", raiz.GetProperty("FechaPrimerPedido").GetString());
                Assert.AreEqual("2026-09-17T00:00:00", raiz.GetProperty("FechaUltimoPedido").GetString());
            }
        }

        private static async Task<ClienteSyncMessage> PublicarYCapturar(IServicioGestorClientes servicio, string numeroCliente)
        {
            ISincronizacionEventPublisher publisher = A.Fake<ISincronizacionEventPublisher>();
            ClienteSyncMessage capturado = null;
            A.CallTo(() => publisher.PublishEventAsync("sincronizacion-tablas", A<object>.Ignored))
                .Invokes((string _, object message) => capturado = message as ClienteSyncMessage);
            var gestor = new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(publisher));

            await gestor.PublicarClienteSincronizar(new Cliente
            {
                Empresa = "1",
                Nº_Cliente = numeroCliente,
                Contacto = "0",
                Nombre = "Cliente Test"
            });

            Assert.IsNotNull(capturado);
            return capturado;
        }

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, IQueryable<T> data) where T : class
        {
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .Returns(new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).Returns(data.GetEnumerator());
        }
    }
}
