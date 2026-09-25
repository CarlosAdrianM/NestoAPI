using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#534: criterio del aviso de facturas vencidas por transferencia (Carlos y Laura,
    /// 24/09/26): umbral de días, fecha de corte, forma de pago, sin prepago ni contado, y el
    /// MISMO gating de entregas que la remesa (#332).
    /// </summary>
    [TestClass]
    public class SelectorAvisosFacturasVencidasTests
    {
        private static readonly DateTime HOY = new DateTime(2026, 9, 24);

        private NVEntities db;
        private DbSet<ExtractoCliente> fakeExtractos;
        private DbSet<CabFacturaVta> fakeFacturas;
        private DbSet<LinPedidoVta> fakeLineas;
        private DbSet<EnviosAgencia> fakeEnvios;
        private DbSet<Cliente> fakeClientes;
        private DbSet<PersonaContactoCliente> fakePersonas;
        private List<string> estadosQueBloquean;
        private List<int> agenciasConSeguimiento;
        private IAlmacenAvisosFacturasVencidas almacen;
        private Dictionary<int, AvisoFacturaVencidaRegistrado> memoria;
        private SelectorAvisosFacturasVencidas selector;

        [TestInitialize]
        public void Setup()
        {
            db = A.Fake<NVEntities>();
            fakeExtractos = Crear<ExtractoCliente>();
            fakeFacturas = Crear<CabFacturaVta>();
            fakeLineas = Crear<LinPedidoVta>();
            fakeEnvios = Crear<EnviosAgencia>();
            fakeClientes = Crear<Cliente>();
            fakePersonas = Crear<PersonaContactoCliente>();
            A.CallTo(() => db.ExtractosCliente).Returns(fakeExtractos);
            A.CallTo(() => db.CabsFacturasVtas).Returns(fakeFacturas);
            A.CallTo(() => db.LinPedidoVtas).Returns(fakeLineas);
            A.CallTo(() => db.EnviosAgencias).Returns(fakeEnvios);
            A.CallTo(() => db.Clientes).Returns(fakeClientes);
            A.CallTo(() => db.PersonasContactoClientes).Returns(fakePersonas);
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>());
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>());
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>());
            ConfigurarFakeDbSet(fakeClientes, new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "15191", Contacto = "0", Nombre = "PELUQUERÍA ANA" },
                new Cliente { Empresa = "1", Nº_Cliente = "30676", Contacto = "0", Nombre = "ESTÉTICA LUZ" }
            });
            ConfigurarFakeDbSet(fakePersonas, new List<PersonaContactoCliente>
            {
                Persona("15191", "facturas@ana.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO),
                Persona("30676", "facturas@luz.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO)
            });
            // Por defecto, cada efecto de los tests tiene su factura a 30 días
            ConfigurarFakeDbSet(fakeFacturas, new List<CabFacturaVta>
            {
                Factura("NV2612000"), Factura("NV2612001"), Factura("NV2612002"), Factura("NV2612003")
            });
            estadosQueBloquean = new List<string>();
            // Los envíos de los tests no fijan Agencia (0): 0 cuenta como agencia con seguimiento
            agenciasConSeguimiento = new List<int> { 0 };
            // NestoAPI#544: la memoria (tabla AvisosFacturasVencidas) va por su interfaz; vacía por defecto
            almacen = A.Fake<IAlmacenAvisosFacturasVencidas>();
            memoria = new Dictionary<int, AvisoFacturaVencidaRegistrado>();
            A.CallTo(() => almacen.UltimoAvisoPorEfecto("1", A<IEnumerable<int>>._))
                .ReturnsLazily((string e, IEnumerable<int> ordenes) => Task.FromResult(
                    memoria.Where(m => ordenes.Contains(m.Key)).ToDictionary(m => m.Key, m => m.Value)));
            selector = new SelectorAvisosFacturasVencidas(db, e => Task.FromResult(estadosQueBloquean),
                () => agenciasConSeguimiento.ToArray(), almacen);
        }

        private static DbSet<T> Crear<T>() where T : class
            => A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());

        private static void ConfigurarFakeDbSet<T>(DbSet<T> fakeDbSet, List<T> lista) where T : class
        {
            IQueryable<T> data = lista.AsQueryable();
            A.CallTo(() => ((IDbAsyncEnumerable<T>)fakeDbSet).GetAsyncEnumerator())
                .ReturnsLazily(() => new TestDbAsyncEnumerator<T>(data.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Provider)
                .Returns(new TestDbAsyncQueryProvider<T>(data.Provider));
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).Expression).Returns(data.Expression);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).ElementType).Returns(data.ElementType);
            A.CallTo(() => ((IQueryable<T>)fakeDbSet).GetEnumerator()).ReturnsLazily(() => data.GetEnumerator());
        }

        // OJO gotcha de fakes (#313): el char() de BD lleva relleno y el fake compara ordinal,
        // así que aquí los datos van SIN espacios.
        private static ExtractoCliente Efecto(int id = 1, string cliente = "15191", decimal pendiente = 250.50m,
            string formaPago = "TRN", DateTime? vencimiento = null, string documento = "NV2612000",
            string tipoApunte = "2", string estado = null)
        {
            DateTime vto = vencimiento ?? HOY.AddDays(-10);
            return new ExtractoCliente
            {
                Empresa = "1",
                Nº_Orden = id,
                Número = cliente,
                Contacto = "0",
                TipoApunte = tipoApunte,
                ImportePdte = pendiente,
                FormaPago = formaPago,
                Fecha = vto.AddDays(-30),
                FechaVto = vto,
                Nº_Documento = documento,
                Efecto = "1",
                Estado = estado
            };
        }

        private static CabFacturaVta Factura(string numero, string plazos = "1/30", DateTime? fecha = null)
            => new CabFacturaVta { Empresa = "1", Número = numero, PlazosPago = plazos, Fecha = fecha ?? HOY.AddDays(-40) };

        private static PersonaContactoCliente Persona(string cliente, string correo, short cargo, short estado = 0,
            string nombre = null, string saludo = null)
            => new PersonaContactoCliente
            {
                Empresa = "1",
                NºCliente = cliente,
                Contacto = "0",
                CorreoElectrónico = correo,
                Cargo = cargo,
                Estado = estado,
                Nombre = nombre,
                Saludo = saludo
            };

        [TestMethod]
        public async Task Candidatos_TransferenciaVencidaHaceDiezDias_SeAvisaConSusDatos()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto(pendiente: 1234.56m) });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            AvisoFacturaVencidaDTO aviso = candidatos.Single();
            Assert.IsTrue(aviso.SeAvisaria, aviso.Motivo);
            Assert.AreEqual("15191", aviso.Cliente);
            Assert.AreEqual("PELUQUERÍA ANA", aviso.Nombre);
            Assert.AreEqual("NV2612000", aviso.Factura);
            Assert.AreEqual(HOY.AddDays(-40), aviso.FechaFactura);
            Assert.AreEqual(HOY.AddDays(-10), aviso.Vencimiento);
            Assert.AreEqual(1234.56m, aviso.Importe);
            Assert.AreEqual(10, aviso.DiasVencida);
            Assert.AreEqual("facturas@ana.es", aviso.Destinatarios);
        }

        [TestMethod]
        public async Task Candidatos_Umbral_SoloLosVencidosHaceNDiasOMas()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, vencimiento: HOY.AddDays(-5)),   // justo en el umbral: entra
                Efecto(id: 2, vencimiento: HOY.AddDays(-4)),   // un día antes: todavía no
                Efecto(id: 3, vencimiento: HOY)                // vence hoy: no
            });

            List<AvisoFacturaVencidaDTO> conCinco = await selector.Candidatos("1", 5, HOY);
            List<AvisoFacturaVencidaDTO> conTres = await selector.Candidatos("1", 3, HOY);

            CollectionAssert.AreEquivalent(new[] { 1 }, conCinco.Select(c => c.NOrden).ToArray());
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, conTres.Select(c => c.NOrden).ToArray(),
                "El umbral es configurable");
        }

        [TestMethod]
        public async Task Candidatos_UmbralNoValido_UsaElDeCincoDias()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, vencimiento: HOY.AddDays(-5)),
                Efecto(id: 2, vencimiento: HOY.AddDays(-1))
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 0, HOY);

            CollectionAssert.AreEquivalent(new[] { 1 }, candidatos.Select(c => c.NOrden).ToArray());
        }

        [TestMethod]
        public async Task Candidatos_FechaDeCorte_LoVencidoAntesDel1DeJulioSigueAMano()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, vencimiento: new DateTime(2026, 6, 30)),
                Efecto(id: 2, vencimiento: new DateTime(2026, 7, 1))
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            CollectionAssert.AreEquivalent(new[] { 2 }, candidatos.Select(c => c.NOrden).ToArray());
        }

        [TestMethod]
        public async Task Candidatos_FormaDePago_SoloTransferencia()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, formaPago: Constantes.FormasPago.RECIBO_BANCARIO),
                Efecto(id: 2, formaPago: Constantes.FormasPago.TARJETA),
                Efecto(id: 3, formaPago: Constantes.FormasPago.EFECTIVO),
                Efecto(id: 4, formaPago: Constantes.FormasPago.TRANSFERENCIA)
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            CollectionAssert.AreEquivalent(new[] { 4 }, candidatos.Select(c => c.NOrden).ToArray());
        }

        [TestMethod]
        public async Task Candidatos_PrepagoYContado_NoEntran()
        {
            // El correo podría llegar antes que el pedido
            ConfigurarFakeDbSet(fakeFacturas, new List<CabFacturaVta>
            {
                Factura("NV2612000", plazos: Constantes.PlazosPago.PREPAGO),
                Factura("NV2612001", plazos: Constantes.PlazosPago.CONTADO),
                Factura("NV2612002", plazos: Constantes.PlazosPago.CONTADO_RIGUROSO),
                Factura("NV2612003", plazos: "1/30")
            });
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2612000"),
                Efecto(id: 2, documento: "NV2612001"),
                Efecto(id: 3, documento: "NV2612002"),
                Efecto(id: 4, documento: "NV2612003")
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            CollectionAssert.AreEquivalent(new[] { 4 }, candidatos.Select(c => c.NOrden).ToArray());
        }

        [TestMethod]
        public async Task Candidatos_SoloCarteraConImportePendientePositivo()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, tipoApunte: "1"),           // la factura, no la cartera
                Efecto(id: 2, pendiente: 0m),             // ya pagado
                Efecto(id: 3)
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            CollectionAssert.AreEquivalent(new[] { 3 }, candidatos.Select(c => c.NOrden).ToArray());
        }

        [TestMethod]
        public async Task Candidatos_GatingDeEntrega_ElMismoQueLaRemesa()
        {
            // #332/#172: si la agencia aún no ha entregado el pedido de la factura, no se avisa
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "NV2612001"),
                Efecto(id: 2, documento: "NV2612002"),
                Efecto(id: 3, documento: "NV2612003", cliente: "30676")
            });
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922001, Nº_Factura = "NV2612001" },
                new LinPedidoVta { Empresa = "1", Número = 922002, Nº_Factura = "NV2612002" },
                new LinPedidoVta { Empresa = "1", Número = 922003, Nº_Factura = "NV2612003" }
            });
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                new EnviosAgencia { Numero = 1, Pedido = 922001, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Fecha = HOY.AddDays(-12) },
                new EnviosAgencia { Numero = 2, Pedido = 922002, Estado = Constantes.Agencias.ESTADO_ENTREGADO, Fecha = HOY.AddDays(-12) },
                new EnviosAgencia { Numero = 3, Pedido = 922003, Estado = Constantes.Agencias.ESTADO_INCIDENTADO, Fecha = HOY.AddDays(-12) }
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            AvisoFacturaVencidaDTO sinEntregar = candidatos.Single(c => c.NOrden == 1);
            Assert.IsFalse(sinEntregar.SeAvisaria);
            StringAssert.Contains(sinEntregar.Motivo, "sin confirmar la entrega");
            Assert.IsTrue(candidatos.Single(c => c.NOrden == 2).SeAvisaria, "Entregado: se avisa");
            StringAssert.Contains(candidatos.Single(c => c.NOrden == 3).Motivo, "INCIDENTADO");
        }

        [TestMethod]
        public async Task Candidatos_EnvioDeAgenciaSinSeguimiento_NoRetiene()
        {
            // Igual que en la remesa (fallo 20/08/26): el 'tramitado' de una agencia sin
            // seguimiento no significa nada
            agenciasConSeguimiento = new List<int> { 6 };
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto(id: 1, documento: "NV2612001") });
            ConfigurarFakeDbSet(fakeLineas, new List<LinPedidoVta>
            {
                new LinPedidoVta { Empresa = "1", Número = 922001, Nº_Factura = "NV2612001" }
            });
            ConfigurarFakeDbSet(fakeEnvios, new List<EnviosAgencia>
            {
                new EnviosAgencia { Numero = 1, Pedido = 922001, Agencia = 9, Estado = Constantes.Agencias.ESTADO_TRAMITADO, Fecha = HOY.AddDays(-12) }
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            Assert.IsTrue(candidatos.Single().SeAvisaria, candidatos.Single().Motivo);
        }

        [TestMethod]
        public async Task Candidatos_EstadoQueBloquea_NoSeAvisa()
        {
            // Retenido, abogado, rehusado...: los mismos estados que frenan la remesa
            estadosQueBloquean.Add("ABG");
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, estado: "ABG"),
                Efecto(id: 2, estado: "NRM", documento: "NV2612001")
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            StringAssert.Contains(candidatos.Single(c => c.NOrden == 1).Motivo, "bloquea la liquidación");
            Assert.IsTrue(candidatos.Single(c => c.NOrden == 2).SeAvisaria);
        }

        [TestMethod]
        public async Task Candidatos_ClienteConCobrosSinLiquidar_NoSeAvisa()
        {
            // Un cobro a cuenta sin liquidar puede ser justo esa transferencia
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, cliente: "15191"),
                Efecto(id: 2, cliente: "30676", documento: "NV2612001"),
                Efecto(id: 3, cliente: "15191", pendiente: -250.50m, tipoApunte: "3", formaPago: null)
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            Assert.AreEqual(SelectorAvisosFacturasVencidas.MOTIVO_CLIENTE_CON_NEGATIVOS, candidatos.Single(c => c.NOrden == 1).Motivo);
            Assert.IsTrue(candidatos.Single(c => c.NOrden == 2).SeAvisaria);
        }

        [TestMethod]
        public async Task Candidatos_SinFacturaOSinCorreo_NoSeAvisaConMotivo()
        {
            ConfigurarFakeDbSet(fakePersonas, new List<PersonaContactoCliente>
            {
                Persona("30676", "otro@luz.es", 5)   // ni cobros ni facturación
            });
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "APUNTE-MANUAL"),
                Efecto(id: 2, cliente: "30676", documento: "NV2612001")
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            Assert.AreEqual(SelectorAvisosFacturasVencidas.MOTIVO_SIN_FACTURA, candidatos.Single(c => c.NOrden == 1).Motivo);
            Assert.AreEqual(SelectorAvisosFacturasVencidas.MOTIVO_SIN_CORREO, candidatos.Single(c => c.NOrden == 2).Motivo);
        }

        [TestMethod]
        public async Task Candidatos_LosQueSeAvisanVanPrimero()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, documento: "APUNTE-MANUAL"),
                Efecto(id: 2, documento: "NV2612001")
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            Assert.AreEqual(2, candidatos.First().NOrden);
        }

        [TestMethod]
        public void ResolverDestinatarios_CobrosAntesQueFacturacion()
        {
            string destinatarios = SelectorAvisosFacturasVencidas.ResolverDestinatarios(new[]
            {
                Persona("15191", "facturas@ana.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO),
                Persona("15191", " cobros@ana.es ", Constantes.Clientes.PersonasContacto.CARGO_COBROS),
                Persona("15191", "COBROS@ana.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS)
            });

            Assert.AreEqual("cobros@ana.es", destinatarios, "Cobros manda; sin repetir el mismo correo");
        }

        [TestMethod]
        public void ResolverDestinatarios_SinCobros_VaAFacturacionYNoAPersonasDeBajaNiAOtrasCualesquiera()
        {
            string destinatarios = SelectorAvisosFacturasVencidas.ResolverDestinatarios(new[]
            {
                Persona("15191", "baja@ana.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS, estado: -1),
                Persona("15191", "facturas@ana.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO),
                Persona("15191", "jefa@ana.es", 5)
            });

            Assert.AreEqual("facturas@ana.es", destinatarios);
            Assert.AreEqual(string.Empty, SelectorAvisosFacturasVencidas.ResolverDestinatarios(new[]
            {
                Persona("15191", "jefa@ana.es", 5)
            }), "Sin cobros ni facturación no se escribe a cualquiera");
        }
        // ---------------------------------------------------------------- NestoAPI#544

        [TestMethod]
        public async Task Candidatos_SinMemoria_TodosSonPrimerAvisoYTocanHoy()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() });

            AvisoFacturaVencidaDTO aviso = (await selector.Candidatos("1", 5, HOY)).Single();

            Assert.AreEqual(1, aviso.NumeroAviso);
            Assert.IsTrue(aviso.TocaHoy);
            Assert.IsNull(aviso.FechaUltimoAviso);
            A.CallTo(() => almacen.UltimoAvisoPorEfecto("1", A<IEnumerable<int>>.That.Matches(o => o.Contains(1)))).MustHaveHappened();
        }

        [TestMethod]
        public async Task Candidatos_ConMemoria_AplicaLaCadenciaPorEfecto()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1),
                Efecto(id: 2, documento: "NV2612001"),
                Efecto(id: 3, cliente: "30676", documento: "NV2612002", pendiente: 40)
            });
            memoria[1] = new AvisoFacturaVencidaRegistrado { NumOrden = 1, NumeroAviso = 1, Fecha = HOY.AddDays(-3), ImportePendiente = 250.50m };
            memoria[2] = new AvisoFacturaVencidaRegistrado { NumOrden = 2, NumeroAviso = 1, Fecha = HOY.AddDays(-10), ImportePendiente = 250.50m };
            memoria[3] = new AvisoFacturaVencidaRegistrado { NumOrden = 3, NumeroAviso = 2, Fecha = HOY.AddDays(-1), ImportePendiente = 100 };

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            AvisoFacturaVencidaDTO reciente = candidatos.Single(c => c.NOrden == 1);
            Assert.IsTrue(reciente.SeAvisaria, "Sigue siendo avisable (sin motivo)...");
            Assert.IsFalse(reciente.TocaHoy, "...pero hoy no le toca");
            Assert.AreEqual(2, reciente.NumeroAviso);
            Assert.AreEqual(HOY.AddDays(7), reciente.FechaSiguienteAviso);

            AvisoFacturaVencidaDTO cumplido = candidatos.Single(c => c.NOrden == 2);
            Assert.IsTrue(cumplido.TocaHoy);
            Assert.AreEqual(2, cumplido.NumeroAviso);

            AvisoFacturaVencidaDTO pagoParcial = candidatos.Single(c => c.NOrden == 3);
            Assert.IsTrue(pagoParcial.ReinicioPorPagoParcial, "Debía 100 y ahora 40");
            Assert.AreEqual(1, pagoParcial.NumeroAviso);
            Assert.IsTrue(pagoParcial.TocaHoy);
        }

        [TestMethod]
        public async Task Candidatos_SiLaTablaDeMemoriaFalla_TodosCuentanComoPrimerAvisoYNoSeCae()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente> { Efecto() });
            A.CallTo(() => almacen.UltimoAvisoPorEfecto(A<string>._, A<IEnumerable<int>>._)).Throws(new Exception("Invalid object name 'AvisosFacturasVencidas'"));

            AvisoFacturaVencidaDTO aviso = (await selector.Candidatos("1", 5, HOY)).Single();

            Assert.AreEqual(1, aviso.NumeroAviso);
            Assert.IsTrue(aviso.TocaHoy);
        }

        [TestMethod]
        public async Task Candidatos_NombreDeLaPersonaDeContacto_SaludoAntesQueNombreYCobrosAntesQueFacturacion()
        {
            ConfigurarFakeDbSet(fakePersonas, new List<PersonaContactoCliente>
            {
                Persona("15191", "facturas@ana.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO, nombre: "Ana López"),
                Persona("15191", "cobros@ana.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS, nombre: "Susana García", saludo: "Susana"),
                Persona("30676", "facturas@luz.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO, nombre: "Luz Martín")
            });
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1),
                Efecto(id: 2, cliente: "30676", documento: "NV2612001")
            });

            List<AvisoFacturaVencidaDTO> candidatos = await selector.Candidatos("1", 5, HOY);

            Assert.AreEqual("Susana", candidatos.Single(c => c.NOrden == 1).NombrePersonaContacto, "La de Cobros, y su Saludo");
            Assert.AreEqual("cobros@ana.es", candidatos.Single(c => c.NOrden == 1).Destinatarios);
            Assert.AreEqual("Luz Martín", candidatos.Single(c => c.NOrden == 2).NombrePersonaContacto, "Sin Saludo, el Nombre");
        }

        [TestMethod]
        public void ResolverNombrePersonaContacto_SinNombreNiSaludo_Null_YNoMezclaGrupos()
        {
            Assert.IsNull(SelectorAvisosFacturasVencidas.ResolverNombrePersonaContacto(new[]
            {
                Persona("15191", "cobros@ana.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS)
            }));
            Assert.IsNull(SelectorAvisosFacturasVencidas.ResolverNombrePersonaContacto(new[]
            {
                Persona("15191", "cobros@ana.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS),
                Persona("15191", "facturas@ana.es", Constantes.Clientes.PersonasContacto.CARGO_FACTURA_POR_CORREO, nombre: "Ana")
            }), "Se escribe a Cobros: no se saluda con el nombre de la de facturación");
            Assert.IsNull(SelectorAvisosFacturasVencidas.ResolverNombrePersonaContacto(new[]
            {
                Persona("15191", "cobros@ana.es", Constantes.Clientes.PersonasContacto.CARGO_COBROS, estado: -1, nombre: "Baja")
            }), "Las personas de baja no cuentan");
            Assert.IsNull(SelectorAvisosFacturasVencidas.ResolverNombrePersonaContacto(null));
        }

        [TestMethod]
        public async Task ApuntesNegativos_DevuelveLosApuntesConPendienteNegativoDeEsosClientes()
        {
            ConfigurarFakeDbSet(fakeExtractos, new List<ExtractoCliente>
            {
                Efecto(id: 1, cliente: "15191"),
                Efecto(id: 2, cliente: "15191", pendiente: -80.25m, tipoApunte: "3", formaPago: null, documento: "TRF"),
                Efecto(id: 3, cliente: "15191", pendiente: -10m, tipoApunte: "2", formaPago: null, documento: "NV2611999"),
                Efecto(id: 4, cliente: "30676", pendiente: -5m, tipoApunte: "3", formaPago: null)
            });

            List<ApunteNegativoClienteDTO> apuntes = await selector.ApuntesNegativos("1", new[] { "15191" });

            Assert.AreEqual(2, apuntes.Count);
            ApunteNegativoClienteDTO pago = apuntes.Single(a => a.NOrden == 2);
            Assert.AreEqual("15191", pago.Cliente);
            Assert.AreEqual("PELUQUERÍA ANA", pago.Nombre);
            Assert.AreEqual(-80.25m, pago.Importe);
            Assert.AreEqual("3", pago.TipoApunte);
            Assert.AreEqual("TRF", pago.Documento);
            Assert.IsFalse(apuntes.Any(a => a.Cliente == "30676"), "Solo los clientes pedidos");
            Assert.AreEqual(0, (await selector.ApuntesNegativos("1", new string[0])).Count);
        }
    }
}
