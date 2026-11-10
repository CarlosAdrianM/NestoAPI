using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Embudo;
using NestoAPI.Infraestructure.Sincronizacion;
using NestoAPI.Models;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// Embudo de Odoo con clientes del vendedor general (odoo-custom-addons#45): campos nuevos del mensaje de cliente, el
    /// responsable por código postal, la republicación por bloques y el paso por la cola de Nesto_sync.
    /// </summary>
    [TestClass]
    public class EmbudoOdooTests
    {
        private const string CORREO_ALBERTO = "alberto@nuevavision.es";

        // ===== Responsable del embudo =====

        [TestMethod]
        public void Responsable_SinVendedorOElGeneral_TiendaOnline()
        {
            foreach (string vendedor in new[] { null, "", "NV", "nv " })
            {
                ResponsableEmbudo r = ReglasResponsableEmbudo.Decidir(vendedor, "x@nuevavision.es", null, CORREO_ALBERTO);
                Assert.AreEqual(ReglasResponsableEmbudo.TIPO_TIENDA_ONLINE, r.Tipo, $"vendedor «{vendedor}»");
                Assert.AreEqual("tiendaonline@nuevavision.es", r.Email);
            }
        }

        [TestMethod]
        public void Responsable_DelEquipoDeAlberto_OElPropioAlberto_ElCorreoDeAlberto()
        {
            ResponsableEmbudo delEquipo = ReglasResponsableEmbudo.Decidir("JE", "je@nuevavision.es", new[] { "ASH" }, CORREO_ALBERTO);
            ResponsableEmbudo alberto = ReglasResponsableEmbudo.Decidir("ASH", CORREO_ALBERTO, new string[0], CORREO_ALBERTO);

            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_EQUIPO_ALBERTO, delEquipo.Tipo);
            Assert.AreEqual(CORREO_ALBERTO, delEquipo.Email);
            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_EQUIPO_ALBERTO, alberto.Tipo);
            Assert.AreEqual(CORREO_ALBERTO, alberto.Email);
        }

        [TestMethod]
        public void Responsable_OtroVendedorConCorreo_ElVendedor_YSinCorreo_TiendaOnline()
        {
            ResponsableEmbudo conCorreo = ReglasResponsableEmbudo.Decidir("RFG", " rfg@nuevavision.es ", new[] { "OTRO" }, CORREO_ALBERTO);
            ResponsableEmbudo sinCorreo = ReglasResponsableEmbudo.Decidir("RFG", "  ", null, CORREO_ALBERTO);

            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_VENDEDOR, conCorreo.Tipo);
            Assert.AreEqual("rfg@nuevavision.es", conCorreo.Email);
            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_TIENDA_ONLINE, sinCorreo.Tipo);
        }

        [TestMethod]
        public async Task Calculador_UsaElVendedorDelCodigoPostalYNoRepiteConsultas()
        {
            IDatosResponsableEmbudo datos = A.Fake<IDatosResponsableEmbudo>();
            A.CallTo(() => datos.VendedorDeCodigoPostal("28001")).Returns("JE");
            A.CallTo(() => datos.JefesDe("JE")).Returns(new List<string> { "ASH" });
            A.CallTo(() => datos.CorreoVendedor("JE")).Returns("je@nuevavision.es");
            A.CallTo(() => datos.CorreoVendedor("ASH")).Returns(CORREO_ALBERTO);
            var calculador = new CalculadorResponsableEmbudo(datos);

            ResponsableEmbudo primero = await calculador.Calcular("28001     ");
            ResponsableEmbudo segundo = await calculador.Calcular("28001");
            ResponsableEmbudo sinCp = await calculador.Calcular(null);

            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_EQUIPO_ALBERTO, primero.Tipo);
            Assert.AreSame(primero, segundo);
            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_TIENDA_ONLINE, sinCp.Tipo);
            A.CallTo(() => datos.VendedorDeCodigoPostal(A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ===== Campos nuevos en TODOS los mensajes de cliente =====

        private static async Task<ClienteSyncMessage> Publicar(IServicioGestorClientes servicio, Cliente cliente, Action<ClienteSyncMessage> completar = null)
        {
            ISincronizacionEventPublisher publisher = A.Fake<ISincronizacionEventPublisher>();
            ClienteSyncMessage capturado = null;
            A.CallTo(() => publisher.PublishEventAsync("sincronizacion-tablas", A<object>.Ignored))
                .Invokes((string _, object message) => capturado = message as ClienteSyncMessage);
            var gestor = new GestorClientes(servicio, A.Fake<IServicioAgencias>(), new SincronizacionEventWrapper(publisher));
            await gestor.PublicarClienteSincronizar(cliente, new FechasComprasCliente(), "Nesto viejo", "usuario", completar);
            Assert.IsNotNull(capturado);
            return capturado;
        }

        private static Cliente ClienteConPersonas()
        {
            var cliente = new Cliente { Empresa = "1  ", Nº_Cliente = "40001     ", Contacto = "0  ", Vendedor = "NV ", CodPostal = "28001" };
            cliente.PersonasContactoClientes.Add(new PersonaContactoCliente { Empresa = "1  ", Número = "1  ", Nombre = "ANA", CorreoElectrónico = "ana@salon.es", EnviarBoletin = true, Estado = 0 });
            cliente.PersonasContactoClientes.Add(new PersonaContactoCliente { Empresa = "1  ", Número = "2  ", Nombre = "LUIS", CorreoElectrónico = "luis@salon.es", EnviarBoletin = false, Estado = 0 });
            return cliente;
        }

        [TestMethod]
        public async Task Mensaje_LlevaElVendedorDePeluqueriaCargadoYElEnviarBoletinDeCadaPersona()
        {
            IServicioGestorClientes servicio = A.Fake<IServicioGestorClientes>();
            Cliente cliente = ClienteConPersonas();
            cliente.VendedoresClienteGrupoProductoes.Add(new VendedorClienteGrupoProducto { GrupoProducto = "PEL", Vendedor = "NV " });

            ClienteSyncMessage mensaje = await Publicar(servicio, cliente);

            Assert.AreEqual("NV", mensaje.VendedorPeluqueria);
            Assert.AreEqual(true, mensaje.PersonasContacto.Single(p => p.Id == "1").EnviarBoletin);
            Assert.AreEqual(false, mensaje.PersonasContacto.Single(p => p.Id == "2").EnviarBoletin);
            A.CallTo(() => servicio.BuscarVendedorGrupo(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Mensaje_SinElGrupoCargado_LoConsulta_YSinFilaNoViaja()
        {
            IServicioGestorClientes conFila = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => conFila.BuscarVendedorGrupo("1", "40001", "0", "PEL"))
                .Returns(new VendedorClienteGrupoProducto { Vendedor = "IF " });
            IServicioGestorClientes sinFila = A.Fake<IServicioGestorClientes>();
            A.CallTo(() => sinFila.BuscarVendedorGrupo(A<string>._, A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult<VendedorClienteGrupoProducto>(null));

            Assert.AreEqual("IF", (await Publicar(conFila, ClienteConPersonas())).VendedorPeluqueria);
            ClienteSyncMessage sinVendedor = await Publicar(sinFila, ClienteConPersonas());
            Assert.IsNull(sinVendedor.VendedorPeluqueria);
            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(sinVendedor)))
            {
                Assert.IsFalse(json.RootElement.TryGetProperty("VendedorPeluqueria", out _), "sin fila: ausente = no tocar");
            }
        }

        [TestMethod]
        public async Task MensajeNormal_NoLlevaLosCamposDelEmbudo_NiSiquieraANull()
        {
            ClienteSyncMessage mensaje = await Publicar(A.Fake<IServicioGestorClientes>(), ClienteConPersonas());

            using (JsonDocument json = JsonDocument.Parse(JsonSerializer.Serialize(mensaje)))
            {
                foreach (string campo in new[] { "BloqueEmbudo", "ResponsableEmbudoEmail", "ResponsableEmbudoTipo" })
                {
                    Assert.IsFalse(json.RootElement.TryGetProperty(campo, out _), campo);
                }
                // Id sigue siendo string; EnviarBoletin viaja en cada persona
                JsonElement persona = json.RootElement.GetProperty("PersonasContacto")[0];
                Assert.AreEqual(JsonValueKind.String, persona.GetProperty("Id").ValueKind);
                Assert.AreEqual(JsonValueKind.True, persona.GetProperty("EnviarBoletin").ValueKind);
                // Las fechas siguen viajando (null explícito) aunque no haya compras
                Assert.AreEqual(JsonValueKind.Null, json.RootElement.GetProperty("FechaUltimoPedido").ValueKind);
            }
        }

        // ===== La cola de Nesto_sync con un bloque pendiente =====

        private class Escenario
        {
            public IGestorClientes Gestor = A.Fake<IGestorClientes>();
            public IRepositorioClientesEmbudo Embudo = A.Fake<IRepositorioClientesEmbudo>();
            public IDatosResponsableEmbudo Datos = A.Fake<IDatosResponsableEmbudo>();
            public List<ClienteSyncMessage> Mensajes = new List<ClienteSyncMessage>();
            public List<Cliente> Contactos;
            public PasadaSincronizacionClientes Pasada;

            public Escenario(string bloque)
            {
                Contactos = new List<Cliente>
                {
                    new Cliente { Empresa = "1", Nº_Cliente = "40001", Contacto = "0", CodPostal = "28001" },
                    new Cliente { Empresa = "1", Nº_Cliente = "40001", Contacto = "1", CodPostal = "08001" }
                };
                A.CallTo(() => Gestor.LeerFechasCompras(A<IEnumerable<string>>._))
                    .Returns(new Dictionary<string, FechasComprasCliente> { ["40001"] = new FechasComprasCliente { FechaUltimoPedido = new DateTime(2024, 3, 1) } });
                A.CallTo(() => Gestor.PublicarClienteSincronizar(A<Cliente>._, A<FechasComprasCliente>._, A<string>._, A<string>._, A<Action<ClienteSyncMessage>>._))
                    .Invokes((Cliente c, FechasComprasCliente f, string s, string u, Action<ClienteSyncMessage> completar) =>
                    {
                        var m = new ClienteSyncMessage { Cliente = c.Nº_Cliente, Contacto = c.Contacto, FechaUltimoPedido = f.FechaUltimoPedido };
                        completar?.Invoke(m);
                        Mensajes.Add(m);
                    });
                A.CallTo(() => Embudo.BloquePendiente("40001")).Returns(bloque);
                A.CallTo(() => Datos.VendedorDeCodigoPostal("28001")).Returns("JE");
                A.CallTo(() => Datos.JefesDe("JE")).Returns(new List<string> { "ASH" });
                A.CallTo(() => Datos.CorreoVendedor("ASH")).Returns(CORREO_ALBERTO);
                A.CallTo(() => Datos.VendedorDeCodigoPostal("08001")).Returns("NV");
                Pasada = new PasadaSincronizacionClientes(Gestor, _ => Task.FromResult(Contactos), Embudo, new CalculadorResponsableEmbudo(Datos));
            }

            public async Task Procesar()
            {
                List<Cliente> entidades = await Pasada.ObtenerEntidades(new NestoSyncRecord { Id = 1, Tabla = "Clientes", ModificadoId = "40001" });
                foreach (Cliente c in entidades)
                {
                    await Pasada.Publicar(c, "usuario");
                }
            }
        }

        [TestMethod]
        public async Task Cola_ClienteConBloquePendiente_CadaContactoConSuResponsable_YSeMarcaEnviadoAlFinal()
        {
            var e = new Escenario("NV-2026-11-01");

            await e.Procesar();

            Assert.AreEqual(2, e.Mensajes.Count);
            Assert.IsTrue(e.Mensajes.All(m => m.BloqueEmbudo == "NV-2026-11-01"));
            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_EQUIPO_ALBERTO, e.Mensajes[0].ResponsableEmbudoTipo);
            Assert.AreEqual(CORREO_ALBERTO, e.Mensajes[0].ResponsableEmbudoEmail);
            Assert.AreEqual(ReglasResponsableEmbudo.TIPO_TIENDA_ONLINE, e.Mensajes[1].ResponsableEmbudoTipo);
            Assert.IsTrue(e.Mensajes.All(m => m.FechaUltimoPedido == new DateTime(2024, 3, 1)), "la fecha del último pedido, como la cola");
            A.CallTo(() => e.Embudo.MarcarEnviado("40001", "NV-2026-11-01")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cola_FallaUnContacto_NoSeMarcaEnviado_YElReintentoVuelveALlevarElBloque()
        {
            var e = new Escenario("NV-2026-11-01");
            A.CallTo(() => e.Gestor.PublicarClienteSincronizar(A<Cliente>.That.Matches(c => c.Contacto == "1"), A<FechasComprasCliente>._,
                    A<string>._, A<string>._, A<Action<ClienteSyncMessage>>._))
                .Throws(new Exception("Pub/Sub")).Once();

            await Assert.ThrowsExceptionAsync<Exception>(() => e.Procesar());
            A.CallTo(() => e.Embudo.MarcarEnviado(A<string>._, A<string>._)).MustNotHaveHappened();

            e.Mensajes.Clear();
            await e.Procesar();
            Assert.AreEqual(2, e.Mensajes.Count);
            Assert.IsTrue(e.Mensajes.All(m => m.BloqueEmbudo == "NV-2026-11-01"));
            A.CallTo(() => e.Embudo.MarcarEnviado("40001", "NV-2026-11-01")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Cola_SinBloquePendiente_SinCamposNiMarca()
        {
            var e = new Escenario(null);

            await e.Procesar();

            Assert.IsTrue(e.Mensajes.All(m => m.BloqueEmbudo == null && m.ResponsableEmbudoEmail == null && m.ResponsableEmbudoTipo == null));
            A.CallTo(() => e.Embudo.MarcarEnviado(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => e.Datos.VendedorDeCodigoPostal(A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Cola_NoSePuedeLeerElRegistro_SePublicaIgualSinLosCampos()
        {
            var e = new Escenario("NV-2026-11-01");
            A.CallTo(() => e.Embudo.BloquePendiente(A<string>._)).Throws(new Exception("sin permiso"));

            await e.Procesar();

            Assert.AreEqual(2, e.Mensajes.Count);
            Assert.IsTrue(e.Mensajes.All(m => m.BloqueEmbudo == null));
        }

        // ===== POST api/Clientes/Sync/Republicar =====

        private static CandidatoEmbudo Candidato(string cliente, bool yaEnviado = false, bool pendiente = false) =>
            new CandidatoEmbudo { Cliente = cliente, Contactos = 2, ContactosActivos = 1, PersonasConCorreo = 1, YaEnviado = yaEnviado, PendienteSincronizar = pendiente };

        private static IRepositorioClientesEmbudo Repositorio(params CandidatoEmbudo[] candidatos)
        {
            IRepositorioClientesEmbudo repo = A.Fake<IRepositorioClientesEmbudo>();
            A.CallTo(() => repo.HayTabla()).Returns(true);
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<IList<short>>._, A<bool>._)).Returns(candidatos.ToList());
            A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>._, A<string>._))
                .ReturnsLazily((string b, IList<string> clientes, string u) => clientes.Count);
            return repo;
        }

        [TestMethod]
        public async Task Republicar_PorDefectoEsEnsayo_NoEscribe_YUsaNVYLosEstadosDeSiempre()
        {
            IRepositorioClientesEmbudo repo = Repositorio(Candidato("40001"), Candidato("40002"));

            ResultadoRepublicarEmbudo r = await new ServicioEmbudoClientes(repo).Republicar(new PeticionRepublicarEmbudo { Bloque = " NV-2026-11-01 " }, "Carlos");

            Assert.IsTrue(r.Ensayo);
            Assert.AreEqual("NV-2026-11-01", r.Bloque);
            Assert.AreEqual(2, r.ClientesEncolados);
            CollectionAssert.AreEqual(new[] { "40001", "40002" }, r.Clientes);
            A.CallTo(() => repo.Registrar(A<string>._, A<IList<string>>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repo.LeerCandidatos("NV", A<IList<short>>.That.Matches(e => e.SequenceEqual(new short[] { 0, 7, 9, 93, 11, 22 })), true))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Republicar_FueraLosYaEnviadosYLosPendientes_TopeYLosQueQuedan()
        {
            IRepositorioClientesEmbudo repo = Repositorio(Candidato("1", yaEnviado: true), Candidato("2"), Candidato("3", pendiente: true),
                Candidato("4"), Candidato("5"), Candidato("6"));

            ResultadoRepublicarEmbudo r = await new ServicioEmbudoClientes(repo).Republicar(
                new PeticionRepublicarEmbudo { Bloque = "B1", Maximo = 2, Ensayo = false }, "Carlos");

            CollectionAssert.AreEqual(new[] { "2", "4" }, r.Clientes);
            Assert.AreEqual(1, r.ExcluidosPorYaEnviados);
            Assert.AreEqual(1, r.ExcluidosPorPendienteSincronizar);
            Assert.AreEqual(2, r.CandidatosQueQuedan);
            Assert.AreEqual(4, r.Contactos);
            Assert.AreEqual(2, r.ContactosActivos);
            Assert.AreEqual(2, r.PersonasConCorreo);
            Assert.AreEqual(2, r.ClientesEncolados);
            A.CallTo(() => repo.Registrar("B1", A<IList<string>>.That.Matches(c => c.SequenceEqual(new[] { "2", "4" })), "Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Republicar_PeticionesQueNoValen()
        {
            var servicio = new ServicioEmbudoClientes(Repositorio());
            foreach (PeticionRepublicarEmbudo p in new[]
            {
                new PeticionRepublicarEmbudo(),
                new PeticionRepublicarEmbudo { Bloque = "B", Maximo = 51 },
                new PeticionRepublicarEmbudo { Bloque = "B", Maximo = 0 },
                new PeticionRepublicarEmbudo { Bloque = "B", Estados = new List<short>() },
                new PeticionRepublicarEmbudo { Bloque = new string('X', 31) },
                new PeticionRepublicarEmbudo { Bloque = "SIEMBRA" }
            })
            {
                await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => servicio.Republicar(p, "Carlos"));
            }
        }

        [TestMethod]
        public async Task Republicar_SinLaTabla_DiceQueFaltaElScript()
        {
            IRepositorioClientesEmbudo repo = Repositorio();
            A.CallTo(() => repo.HayTabla()).Returns(false);

            var ex = await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() =>
                new ServicioEmbudoClientes(repo).Republicar(new PeticionRepublicarEmbudo { Bloque = "B" }, "Carlos"));

            StringAssert.Contains(ex.Message, "Falta lanzar el script");
            A.CallTo(() => repo.LeerCandidatos(A<string>._, A<IList<short>>._, A<bool>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void SqlCandidatos_ParametrosDeLosEstadosYOrdenSinPedidosPrimero()
        {
            string sql = RepositorioClientesEmbudoSql.SqlCandidatos(3);

            StringAssert.Contains(sql, "c.Estado IN (@e0, @e1, @e2)");
            // Desempate por Nº de cliente en orden numérico (LEN ignora los espacios del char(10))
            StringAssert.Contains(sql, "ORDER BY CASE WHEN t.UltimoPedido IS NULL THEN 0 ELSE 1 END, t.UltimoPedido, LEN(t.Cliente), t.Cliente");
            StringAssert.Contains(sql, "l.Estado >= @pendiente");
            StringAssert.Contains(sql, "v.Vendedor <> @vendedorGeneral");
            StringAssert.Contains(sql, "p.EnviarBoletin = 1");
        }

        // ===== Siembra del registro =====

        [TestMethod]
        public async Task Sembrar_SinDuplicados_DiceLosQueNoExistenYLosQueYaEstaban_EnEnsayoNoEscribe()
        {
            IRepositorioClientesEmbudo repo = Repositorio();
            A.CallTo(() => repo.EstadoSiembra(A<IList<string>>._)).Returns(new List<EstadoSiembraEmbudo>
            {
                new EstadoSiembraEmbudo { Cliente = "1", Existe = true, YaEstaba = false },
                new EstadoSiembraEmbudo { Cliente = "2", Existe = true, YaEstaba = true },
                new EstadoSiembraEmbudo { Cliente = "3", Existe = false }
            });
            var fecha = new DateTime(2026, 10, 15);
            var peticion = new PeticionSembrarEmbudo
            {
                Filas = new List<FilaSiembraEmbudo>
                {
                    new FilaSiembraEmbudo { Cliente = "1", Fecha = fecha, Origen = "Odoo-Web" },
                    new FilaSiembraEmbudo { Cliente = " 1 ", Fecha = fecha, Origen = "Odoo-Otra" },
                    new FilaSiembraEmbudo { Cliente = "2", Fecha = fecha, Origen = "Odoo-Web" },
                    new FilaSiembraEmbudo { Cliente = "3", Fecha = fecha, Origen = "Odoo-Web" }
                }
            };

            ResultadoSembrarEmbudo ensayo = await new ServicioEmbudoClientes(repo).Sembrar(peticion, "Carlos");

            Assert.IsTrue(ensayo.Ensayo);
            Assert.AreEqual(4, ensayo.Recibidas);
            Assert.AreEqual(1, ensayo.Nuevas);
            Assert.AreEqual(1, ensayo.YaEstaban);
            CollectionAssert.AreEqual(new[] { "3" }, ensayo.NoExisten);
            A.CallTo(() => repo.Sembrar(A<IList<FilaSiembraEmbudo>>._, A<string>._)).MustNotHaveHappened();

            peticion.Ensayo = false;
            A.CallTo(() => repo.Sembrar(A<IList<FilaSiembraEmbudo>>._, A<string>._)).Returns(1);
            _ = await new ServicioEmbudoClientes(repo).Sembrar(peticion, "Carlos");
            A.CallTo(() => repo.Sembrar(A<IList<FilaSiembraEmbudo>>.That.Matches(f => f.Count == 1 && f[0].Cliente == "1" && f[0].Origen == "Odoo-Web"), "Carlos"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Sembrar_FilaSinFechaOSinOrigen_NoVale()
        {
            var servicio = new ServicioEmbudoClientes(Repositorio());
            await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => servicio.Sembrar(new PeticionSembrarEmbudo
            {
                Filas = new List<FilaSiembraEmbudo> { new FilaSiembraEmbudo { Cliente = "1", Origen = "Odoo" } }
            }, "Carlos"));
            await Assert.ThrowsExceptionAsync<PeticionEmbudoNoValidaException>(() => servicio.Sembrar(new PeticionSembrarEmbudo
            {
                Filas = new List<FilaSiembraEmbudo> { new FilaSiembraEmbudo { Cliente = "1", Fecha = DateTime.Today } }
            }, "Carlos"));
        }

        // ===== Permisos =====

        [TestMethod]
        public void Permiso_SoloDireccionOInformatica()
        {
            IPrincipal Con(params string[] grupos) => new GenericPrincipal(new GenericIdentity(@"NUEVAVISION\Alguien", "Bearer"), grupos);

            Assert.IsTrue(ClientesController.PuedeUsarEmbudo(Con(Constantes.GruposSeguridad.DIRECCION)));
            Assert.IsTrue(ClientesController.PuedeUsarEmbudo(Con(Constantes.GruposSeguridad.INFORMATICA)));
            Assert.IsFalse(ClientesController.PuedeUsarEmbudo(Con(Constantes.GruposSeguridad.ADMINISTRACION)));
            Assert.IsFalse(ClientesController.PuedeUsarEmbudo(Con(Constantes.GruposSeguridad.VENDEDORES)));
            Assert.IsFalse(ClientesController.PuedeUsarEmbudo(null));
        }
    }
}
