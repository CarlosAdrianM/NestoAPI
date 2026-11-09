using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Controllers;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Rapports;
using NestoAPI.Infraestructure.Vendedores;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Tests.Helpers;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Web.Http.Results;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>NestoAPI#623: origen del cliente (catálogo, PUT, alta, GET del cliente), plazo de llamada, ranking y job.</summary>
    [TestClass]
    public class OrigenesClienteTests
    {
        private const string META = "META-HARMONIA-0926";
        // Jueves 05/11/26, 12:00.
        private static readonly DateTime AHORA = new DateTime(2026, 11, 5, 12, 0, 0);

        private IRepositorioOrigenesCliente repositorio;
        private List<string> guardados;

        [TestInitialize]
        public void Preparar()
        {
            guardados = new List<string>();
            repositorio = A.Fake<IRepositorioOrigenesCliente>();
            A.CallTo(() => repositorio.LeerCatalogo()).Returns(Task.FromResult(new List<OrigenCliente>
            {
                new OrigenCliente { Codigo = META, Descripcion = "Lead de Meta · campaña Harmonia", Campana = "Harmonia Meta Ads", Activo = true, HorasParaLlamar = 24 },
                new OrigenCliente { Codigo = "FERIA-2025", Descripcion = "Feria 2025", Activo = false, HorasParaLlamar = 48 },
                new OrigenCliente { Codigo = "CADUCADO", Descripcion = "Caducado", Activo = true, FechaHasta = new DateTime(2026, 10, 31), HorasParaLlamar = 24 }
            }));
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<string>._, A<string>._))
                .ReturnsLazily((string e, string c, string o, string u) => { guardados.Add($"{e}|{c}|{o ?? "null"}|{u}"); return Task.CompletedTask; });
        }

        private static IPrincipal Usuario(string nombre, params string[] grupos)
        {
            var claims = new List<Claim> { new Claim(ClaimTypes.Name, "NUEVAVISION\\" + nombre) };
            foreach (string g in grupos)
            {
                claims.Add(new Claim(ClaimTypes.Role, g));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "prueba"));
        }

        private ClientesController Controlador(IGestorClientes gestor = null, NVEntities db = null)
        {
            var controlador = new ClientesController(gestor ?? A.Fake<IGestorClientes>(), A.Fake<IServicioVendedores>(), A.Fake<IGestorSincronizacion>(), dbInyectada: db)
            {
                RepositorioOrigenes = repositorio,
                RelojOrigenes = () => AHORA
            };
            controlador.User = Usuario("Carlos", Constantes.GruposSeguridad.DIRECCION);
            return controlador;
        }

        // ---------------- PlazoLlamadaLead ----------------

        private static bool SinFestivos(DateTime d) => ReglasLeadsCampana.EsLaborableSinFestivos(d);

        [TestMethod]
        public void PlazoLlamadaLead_MananaEntreSemana_MismaHoraDelDiaSiguiente()
        {
            Assert.AreEqual(new DateTime(2026, 11, 4, 10, 0, 0), ReglasLeadsCampana.PlazoLlamadaLead(new DateTime(2026, 11, 3, 10, 0, 0), 24, SinFestivos));
        }

        [TestMethod]
        public void PlazoLlamadaLead_ViernesALas16_ElLunesALas16()
        {
            Assert.AreEqual(new DateTime(2026, 11, 9, 16, 0, 0), ReglasLeadsCampana.PlazoLlamadaLead(new DateTime(2026, 11, 6, 16, 0, 0), 24, SinFestivos));
        }

        [TestMethod]
        public void PlazoLlamadaLead_VisperaDeFestivo_SaltaElFestivo()
        {
            // Lunes 07/12/26 16:00 con el martes 08/12 (Inmaculada) festivo → miércoles 09/12 16:00.
            Func<DateTime, bool> laborable = ReglasLeadsCampana.Laborable((d, delegacion) => d == new DateTime(2026, 12, 8), "ALG");
            Assert.AreEqual(new DateTime(2026, 12, 9, 16, 0, 0), ReglasLeadsCampana.PlazoLlamadaLead(new DateTime(2026, 12, 7, 16, 0, 0), 24, laborable));
        }

        [TestMethod]
        public void PlazoLlamadaLead_Sabado_ElLunesALaMismaHora()
        {
            Assert.AreEqual(new DateTime(2026, 11, 9, 10, 0, 0), ReglasLeadsCampana.PlazoLlamadaLead(new DateTime(2026, 11, 7, 10, 0, 0), 24, SinFestivos));
        }

        [TestMethod]
        public void PlazoLlamadaLead_48HorasDesdeElJueves_CuentaSoloLaborables()
        {
            Assert.AreEqual(new DateTime(2026, 11, 9, 16, 0, 0), ReglasLeadsCampana.PlazoLlamadaLead(new DateTime(2026, 11, 5, 16, 0, 0), 48, SinFestivos));
        }

        [TestMethod]
        public void Frase_EnPlazo_Y_Atrasada()
        {
            var lead = new LeadCampana { Origen = META };
            DateTime plazo = new DateTime(2026, 11, 9, 16, 0, 0);
            Assert.AreEqual("Lead de Meta: llamar antes de las 16:00 del 09/11", ReglasLeadsCampana.Frase(lead, plazo, plazo.AddMinutes(-1)));
            Assert.AreEqual("Lead de Meta: llamada atrasada (desde el 09/11 16:00)", ReglasLeadsCampana.Frase(lead, plazo, plazo.AddMinutes(1)));
        }

        // ---------------- Catálogo y PUT ----------------

        [TestMethod]
        public async Task GetOrigenes_SoloActivosYSinCaducar_SalvoTodos()
        {
            var activos = (OkNegotiatedContentResult<List<OrigenClienteDTO>>)await Controlador().GetOrigenes();
            CollectionAssert.AreEqual(new[] { META }, activos.Content.Select(o => o.Codigo).ToArray());
            Assert.AreEqual(24, activos.Content[0].HorasParaLlamar);
            Assert.AreEqual("Harmonia Meta Ads", activos.Content[0].Campana);

            var todos = (OkNegotiatedContentResult<List<OrigenClienteDTO>>)await Controlador().GetOrigenes(todos: true);
            Assert.AreEqual(3, todos.Content.Count);
        }

        [TestMethod]
        public async Task GetOrigenes_SinTabla_ListaVacia()
        {
            A.CallTo(() => repositorio.LeerCatalogo()).Returns(Task.FromResult(new List<OrigenCliente>()));
            var vacio = (OkNegotiatedContentResult<List<OrigenClienteDTO>>)await Controlador().GetOrigenes();
            Assert.AreEqual(0, vacio.Content.Count);
        }

        [TestMethod]
        public async Task PutOrigen_Activo_LoGuardaConElUsuario()
        {
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente
            {
                Origen = META, Descripcion = "Lead de Meta · campaña Harmonia", FechaOrigen = AHORA, UsuarioOrigen = "NUEVAVISION\\Carlos"
            }));
            var ok = (OkNegotiatedContentResult<OrigenDeClienteDTO>)await Controlador().PutOrigenCliente("1", "42109 ", new PeticionOrigenClienteDTO { Origen = "meta-harmonia-0926" });
            Assert.AreEqual(META, ok.Content.Origen);
            Assert.AreEqual("Lead de Meta · campaña Harmonia", ok.Content.Descripcion);
            CollectionAssert.AreEqual(new[] { "1|42109|" + META + "|NUEVAVISION\\Carlos" }, guardados);
        }

        [TestMethod]
        public async Task PutOrigen_InactivoOCaducadoOInexistente_400SinGuardar()
        {
            foreach (string codigo in new[] { "FERIA-2025", "CADUCADO", "NO-EXISTE" })
            {
                IHttpActionResultAssert(await Controlador().PutOrigenCliente("1", "42109", new PeticionOrigenClienteDTO { Origen = codigo }));
            }
            Assert.AreEqual(0, guardados.Count);

            void IHttpActionResultAssert(System.Web.Http.IHttpActionResult r) => Assert.IsInstanceOfType(r, typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task PutOrigen_Vacio_LoQuita()
        {
            var ok = (OkNegotiatedContentResult<OrigenDeClienteDTO>)await Controlador().PutOrigenCliente("1", "42109", new PeticionOrigenClienteDTO { Origen = " " });
            Assert.IsNull(ok.Content.Origen);
            CollectionAssert.AreEqual(new[] { "1|42109|null|NUEVAVISION\\Carlos" }, guardados);
        }

        [TestMethod]
        public async Task PutOrigen_Roles_VendedoresYTiendasSi_AlmacenNo()
        {
            ClientesController controlador = Controlador();
            controlador.User = Usuario("Jesus", "Vendedores");
            Assert.IsInstanceOfType(await controlador.PutOrigenCliente("1", "42109", new PeticionOrigenClienteDTO { Origen = META }), typeof(OkNegotiatedContentResult<OrigenDeClienteDTO>));
            controlador.User = Usuario("Reina", Constantes.GruposSeguridad.TIENDAS);
            Assert.IsInstanceOfType(await controlador.PutOrigenCliente("1", "42109", new PeticionOrigenClienteDTO { Origen = META }), typeof(OkNegotiatedContentResult<OrigenDeClienteDTO>));
            controlador.User = Usuario("Andre", Constantes.GruposSeguridad.ALMACEN);
            var prohibido = (NegotiatedContentResult<string>)await controlador.PutOrigenCliente("1", "42109", new PeticionOrigenClienteDTO { Origen = META });
            Assert.AreEqual(HttpStatusCode.Forbidden, prohibido.StatusCode);
            Assert.AreEqual(2, guardados.Count);
        }

        [TestMethod]
        public async Task GetOrigenCliente_SinOrigen_204_ConOrigen_200()
        {
            A.CallTo(() => repositorio.LeerDeCliente("1", "1")).Returns(Task.FromResult<OrigenAsignadoCliente>(null));
            Assert.AreEqual(HttpStatusCode.NoContent, ((StatusCodeResult)await Controlador().GetOrigenCliente("1", "1")).StatusCode);

            A.CallTo(() => repositorio.LeerDeCliente("1", "2")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = META + " ", Descripcion = "d", FechaOrigen = AHORA, UsuarioOrigen = "u " }));
            var ok = (OkNegotiatedContentResult<OrigenDeClienteDTO>)await Controlador().GetOrigenCliente("1", "2");
            Assert.AreEqual(META, ok.Content.Origen);
            Assert.AreEqual(AHORA, ok.Content.FechaOrigen);
            Assert.AreEqual("u", ok.Content.UsuarioOrigen);
        }

        // ---------------- Alta y modificación con Origen ----------------

        private static IGestorClientes GestorQueGuarda()
        {
            IGestorClientes gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._)).Returns(new Cliente { Empresa = "1  ", Nº_Cliente = "42109     ", Contacto = "0  " });
            A.CallTo(() => gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._)).Returns(new Cliente { Empresa = "1  ", Nº_Cliente = "42109     ", Contacto = "0  " });
            return gestor;
        }

        private static ClienteCrear Alta(string origen) => new ClienteCrear
        {
            Empresa = "1",
            Nombre = "PELUQUERÍA LEAD",
            Origen = origen,
            PersonasContacto = new List<PersonaContactoDTO>()
        };

        [TestMethod]
        public async Task PostCliente_ConOrigen_LoGrabaTrasElAlta()
        {
            IGestorClientes gestor = GestorQueGuarda();
            System.Web.Http.IHttpActionResult resultado = await Controlador(gestor).PostCliente(Alta(META));
            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            CollectionAssert.AreEqual(new[] { "1|42109|" + META + "|NUEVAVISION\\Carlos" }, guardados);
        }

        [TestMethod]
        public async Task PostCliente_SinOrigen_NoTocaLaTabla()
        {
            _ = await Controlador(GestorQueGuarda()).PostCliente(Alta(null));
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.LeerCatalogo()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCliente_OrigenInactivo_400SinDarDeAlta()
        {
            IGestorClientes gestor = GestorQueGuarda();
            var resultado = await Controlador(gestor).PostCliente(Alta("FERIA-2025")) as BadRequestErrorMessageResult;
            Assert.IsNotNull(resultado);
            A.CallTo(() => gestor.CrearCliente(A<ClienteCrear>._, A<NVEntities>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PostCliente_FallaElOrigen_ElClienteQuedaCreadoYVaAElmah()
        {
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("sin tabla"));
            var errores = new List<Exception>();
            ClientesController controlador = Controlador(GestorQueGuarda());
            controlador.RegistrarErrorOrigen = errores.Add;
            System.Web.Http.IHttpActionResult resultado = await controlador.PostCliente(Alta(META));
            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "42109");
        }

        [TestMethod]
        public async Task PutCliente_MismoOrigen_NoLoVuelveAGrabar_Distinto_Si()
        {
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = "OTRO" }));
            _ = await Controlador(GestorQueGuarda()).PutCliente(Alta(META));
            CollectionAssert.AreEqual(new[] { "1|42109|" + META + "|NUEVAVISION\\Carlos" }, guardados);
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = META }));
            _ = await Controlador(GestorQueGuarda()).PutCliente(Alta(META));
            Assert.AreEqual(1, guardados.Count);
        }

        [TestMethod]
        public async Task PutCliente_OrigenNullOVacio_NoTocaElOrigen()
        {
            // Nesto no siempre conoce el origen al modificar: null (o vacío) = no tocar. Para quitarlo, PUT .../Origen.
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = META }));
            _ = await Controlador(GestorQueGuarda()).PutCliente(Alta(null));
            _ = await Controlador(GestorQueGuarda()).PutCliente(Alta(""));
            A.CallTo(() => repositorio.Guardar(A<string>._, A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task PutCliente_ConSuOrigenYaInactivo_SeModificaIgual()
        {
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = "FERIA-2025" }));
            IGestorClientes gestor = GestorQueGuarda();
            ClienteCrear modificacion = Alta("FERIA-2025");
            modificacion.Cliente = "42109";
            System.Web.Http.IHttpActionResult resultado = await Controlador(gestor).PutCliente(modificacion);
            Assert.IsNotInstanceOfType(resultado, typeof(BadRequestErrorMessageResult));
            A.CallTo(() => gestor.ModificarCliente(A<ClienteCrear>._, A<NVEntities>._)).MustHaveHappenedOnceExactly();
            Assert.AreEqual(0, guardados.Count);

            // Cambiar a otro inactivo, sí da 400.
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = META }));
            Assert.IsInstanceOfType(await Controlador(gestor).PutCliente(modificacion), typeof(BadRequestErrorMessageResult));
        }

        [TestMethod]
        public async Task GetClienteCrear_TraeElOrigen()
        {
            IGestorClientes gestor = A.Fake<IGestorClientes>();
            A.CallTo(() => gestor.ConstruirClienteCrear("1", "42109", "0")).Returns(new ClienteCrear { Empresa = "1", Nombre = "X" });
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente { Origen = META + "  " }));
            var ok = (OkNegotiatedContentResult<ClienteCrear>)await Controlador(gestor).GetClienteCrear("1", "42109", "0");
            Assert.AreEqual(META, ok.Content.Origen);
        }

        // ---------------- GET api/Clientes con Origen ----------------

        private static DbSet<T> FakeDbSet<T>(List<T> datos) where T : class
        {
            DbSet<T> set = A.Fake<DbSet<T>>(o => o.Implements<IQueryable<T>>().Implements<IDbAsyncEnumerable<T>>());
            A.CallTo(() => ((IDbAsyncEnumerable<T>)set).GetAsyncEnumerator()).ReturnsLazily(() => new TestDbAsyncEnumerator<T>(datos.GetEnumerator()));
            A.CallTo(() => ((IQueryable<T>)set).Provider).ReturnsLazily(() => new TestDbAsyncQueryProvider<T>(datos.AsQueryable().Provider));
            A.CallTo(() => ((IQueryable<T>)set).Expression).ReturnsLazily(() => datos.AsQueryable().Expression);
            A.CallTo(() => ((IQueryable<T>)set).ElementType).Returns(typeof(T));
            A.CallTo(() => ((IQueryable<T>)set).GetEnumerator()).ReturnsLazily(() => datos.GetEnumerator());
            return set;
        }

        [TestMethod]
        public async Task GetCliente_LlevaElOrigenYSuDescripcion()
        {
            NVEntities db = A.Fake<NVEntities>();
            A.CallTo(() => db.Clientes).Returns(FakeDbSet(new List<Cliente>
            {
                new Cliente { Empresa = "1", Nº_Cliente = "42109", Contacto = "0", ClientePrincipal = true, Estado = 0, Nombre = "PELUQUERÍA LEAD" }
            }));
            A.CallTo(() => db.VendedoresClientesGruposProductos).Returns(FakeDbSet(new List<VendedorClienteGrupoProducto>()));
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Returns(Task.FromResult(new OrigenAsignadoCliente
            {
                Origen = META, Descripcion = "Lead de Meta · campaña Harmonia", FechaOrigen = AHORA
            }));
            ClientesController controlador = Controlador(db: db);
            controlador.ClienteExigeSuPedido = (e, c) => false;

            var ok = (OkNegotiatedContentResult<ClienteDTO>)await controlador.GetCliente("1", "42109", "0");

            Assert.AreEqual(META, ok.Content.Origen);
            Assert.AreEqual("Lead de Meta · campaña Harmonia", ok.Content.OrigenDescripcion);

            // Sin origen (o sin tabla, o si falla la lectura): null, y la ficha sale igual.
            A.CallTo(() => repositorio.LeerDeCliente("1", "42109")).Throws(new InvalidOperationException("sin tabla"));
            var sinOrigen = (OkNegotiatedContentResult<ClienteDTO>)await controlador.GetCliente("1", "42109", "0");
            Assert.IsNull(sinOrigen.Content.Origen);
            Assert.IsNull(sinOrigen.Content.OrigenDescripcion);
        }

        // ---------------- Ranking (ImpulsoLeadsCampanaContacto) ----------------

        private static LeadCampana Lead(string cliente, DateTime fechaOrigen, DateTime? ultimoRapport = null, DateTime? avisado = null) => new LeadCampana
        {
            Empresa = "1",
            Cliente = cliente,
            Contacto = "0",
            Nombre = "Lead " + cliente,
            Telefono = "600000000",
            Vendedor = "JE",
            Origen = META,
            Descripcion = "Lead de Meta · campaña Harmonia",
            FechaOrigen = fechaOrigen,
            HorasParaLlamar = 24,
            UltimoRapport = ultimoRapport,
            FechaAvisoRetraso = avisado
        };

        private static List<SugerenciaContactoDTO> Lista(params string[] clientes) =>
            clientes.Select((c, i) => new SugerenciaContactoDTO { Cliente = c, Contacto = "0", Orden = i + 1, Prioridad = PrioridadesContacto.ALTA }).ToList();

        [TestMethod]
        public void Ranking_LeadSinRapport_SaleElPrimeroConElTexto()
        {
            List<SugerenciaContactoDTO> resultado = ImpulsoLeadsCampanaContacto.Aplicar(Lista("1001", "1002"),
                new[] { Lead("42109", new DateTime(2026, 11, 5, 10, 0, 0)) }, AHORA, SinFestivos);

            CollectionAssert.AreEqual(new[] { "42109", "1001", "1002" }, resultado.Select(s => s.Cliente).ToArray());
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, resultado.Select(s => s.Orden).ToArray());
            Assert.AreEqual("Lead de Meta: llamar antes de las 10:00 del 06/11", resultado[0].Motivo);
            Assert.AreEqual(PrioridadesContacto.MAXIMA, resultado[0].Prioridad);
            Assert.AreEqual(ImpulsoLeadsCampanaContacto.ORIGEN, resultado[0].Origen);
            Assert.AreEqual("600000000", resultado[0].Telefono);
        }

        [TestMethod]
        public void Ranking_ConRapportDesdeElOrigen_Desaparece()
        {
            List<SugerenciaContactoDTO> resultado = ImpulsoLeadsCampanaContacto.Aplicar(Lista("1001"),
                new[] { Lead("42109", new DateTime(2026, 11, 5, 10, 0, 0), ultimoRapport: new DateTime(2026, 11, 5)) }, AHORA, SinFestivos);
            CollectionAssert.AreEqual(new[] { "1001" }, resultado.Select(s => s.Cliente).ToArray());
        }

        [TestMethod]
        public void Ranking_RapportAnteriorAlOrigen_NoCuenta()
        {
            List<SugerenciaContactoDTO> resultado = ImpulsoLeadsCampanaContacto.Aplicar(Lista("1001"),
                new[] { Lead("42109", new DateTime(2026, 11, 5, 10, 0, 0), ultimoRapport: new DateTime(2026, 9, 1)) }, AHORA, SinFestivos);
            Assert.AreEqual("42109", resultado[0].Cliente);
        }

        [TestMethod]
        public void Ranking_PlazoPasado_LlamadaAtrasadaYSigueArriba_ElQueAntesVencePrimero()
        {
            List<SugerenciaContactoDTO> resultado = ImpulsoLeadsCampanaContacto.Aplicar(Lista("1001", "42110"),
                new[] { Lead("42110", new DateTime(2026, 11, 5, 9, 0, 0)), Lead("42109", new DateTime(2026, 11, 3, 9, 0, 0)) }, AHORA, SinFestivos);

            CollectionAssert.AreEqual(new[] { "42109", "42110", "1001" }, resultado.Select(s => s.Cliente).ToArray(), "sin repetir el que ya estaba en la lista");
            Assert.AreEqual("Lead de Meta: llamada atrasada (desde el 04/11 09:00)", resultado[0].Motivo);
            Assert.AreEqual("Lead de Meta: llamar antes de las 09:00 del 06/11", resultado[1].Motivo);
        }

        [TestMethod]
        public void Ranking_LeadDeMasDe30Dias_NoSale()
        {
            List<SugerenciaContactoDTO> resultado = ImpulsoLeadsCampanaContacto.Aplicar(Lista("1001"),
                new[] { Lead("42109", AHORA.AddDays(-31)) }, AHORA, SinFestivos);
            CollectionAssert.AreEqual(new[] { "1001" }, resultado.Select(s => s.Cliente).ToArray());
        }

        // ---------------- Job leads-sin-llamar ----------------

        private IDestinatariosLeadsSinLlamar destinatarios;
        private IServicioNotificacionesPush push;

        private LeadsSinLlamarJobsService Job(params LeadCampana[] leads)
        {
            A.CallTo(() => repositorio.LeerLeads(null)).Returns(Task.FromResult(leads.ToList()));
            destinatarios = A.Fake<IDestinatariosLeadsSinLlamar>();
            A.CallTo(() => destinatarios.JefesDe("1", "JE")).Returns(new List<string> { "JV" });
            A.CallTo(() => destinatarios.UsuariosDeVendedores(A<IEnumerable<string>>._))
                .ReturnsLazily((IEnumerable<string> v) => v.Select(x => x == "JE" ? "Jesus" : x == "JV" ? "JefeVentas" : "otro").ToList());
            A.CallTo(() => destinatarios.UsuariosDireccion()).Returns(new List<string> { "Manuel" });
            push = A.Fake<IServicioNotificacionesPush>();
            return new LeadsSinLlamarJobsService(repositorio, destinatarios, push, SinFestivos);
        }

        [TestMethod]
        public async Task Job_LeadVencidoSinRapport_AvisaAlVendedorYAlJefeYMarca()
        {
            LeadsSinLlamarJobsService job = Job(Lead("42109", new DateTime(2026, 11, 3, 9, 0, 0)));

            Assert.AreEqual(1, await job.AvisarAsync(AHORA));

            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Jesus", Constantes.Aplicaciones.NESTO,
                A<NotificacionPushDTO>.That.Matches(n => n.Cuerpo == "El lead de Meta 42109 Lead 42109 lleva más de 24 h sin llamar"))).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\JefeVentas", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Manuel", A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => push.EnviarAVendedor("1", "JE", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAVendedor("1", "JV", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.MarcarAvisoRetraso("1", "42109", AHORA)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_YaAvisado_EnPlazo_OConRapport_NoAvisa()
        {
            LeadsSinLlamarJobsService job = Job(
                Lead("1", new DateTime(2026, 11, 3, 9, 0, 0), avisado: new DateTime(2026, 11, 4, 10, 0, 0)),
                Lead("2", new DateTime(2026, 11, 5, 9, 0, 0)),
                Lead("3", new DateTime(2026, 11, 3, 9, 0, 0), ultimoRapport: new DateTime(2026, 11, 4)));

            Assert.AreEqual(0, await job.AvisarAsync(AHORA));
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.MarcarAvisoRetraso(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Job_VendedorSinJefe_AvisaADireccion()
        {
            LeadCampana lead = Lead("42109", new DateTime(2026, 11, 3, 9, 0, 0));
            lead.Vendedor = "SJ";
            LeadsSinLlamarJobsService job = Job(lead);
            A.CallTo(() => destinatarios.JefesDe("1", "SJ")).Returns(new List<string>());

            Assert.AreEqual(1, await job.AvisarAsync(AHORA));
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Manuel", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => push.EnviarAVendedor("1", "SJ", A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Job_FinDeSemana_NoHaceNada()
        {
            LeadsSinLlamarJobsService job = Job(Lead("42109", new DateTime(2026, 11, 3, 9, 0, 0)));
            Assert.AreEqual(0, await job.AvisarAsync(new DateTime(2026, 11, 7, 12, 0, 0)));
            A.CallTo(() => repositorio.LeerLeads(A<string>._)).MustNotHaveHappened();
        }

        // ---------------- NestoAPI#848: orígenes de canal (no son leads) y origen propuesto por usuario ----------------

        private const string TIENDA_ALCOBENDAS = "TIENDA-ALCOBENDAS";
        private const string CLAVE_PROPUESTO = "OrigenClientePorDefecto";

        private void CatalogoConCanales()
        {
            A.CallTo(() => repositorio.LeerCatalogo()).Returns(Task.FromResult(new List<OrigenCliente>
            {
                new OrigenCliente { Codigo = META, Descripcion = "Lead de Meta · campaña Harmonia", Campana = "Harmonia Meta Ads", Activo = true, HorasParaLlamar = 24, EsLead = true },
                new OrigenCliente { Codigo = TIENDA_ALCOBENDAS, Descripcion = "Tienda de Alcobendas", Activo = true, HorasParaLlamar = 24, EsLead = false },
                new OrigenCliente { Codigo = "WEB", Descripcion = "Página web", Activo = true, HorasParaLlamar = 24, EsLead = false },
                new OrigenCliente { Codigo = "FERIA-2025", Descripcion = "Feria 2025", Activo = false, HorasParaLlamar = 48, EsLead = false }
            }));
        }

        private ClientesController ControladorDe(string usuario, string propioDelUsuario, string deDefecto)
        {
            CatalogoConCanales();
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametroSinDefecto("1", "NUEVAVISION\\" + usuario, CLAVE_PROPUESTO)).Returns(propioDelUsuario);
            A.CallTo(() => lector.LeerParametroSinDefecto("1", "(defecto)", CLAVE_PROPUESTO)).Returns(deDefecto);
            ClientesController controlador = Controlador();
            controlador.LectorParametrosOrigen = lector;
            controlador.User = Usuario(usuario, Constantes.GruposSeguridad.TIENDAS);
            return controlador;
        }

        private static List<OrigenClienteDTO> Contenido(System.Web.Http.IHttpActionResult r) => ((OkNegotiatedContentResult<List<OrigenClienteDTO>>)r).Content;

        [TestMethod]
        public void OrigenCliente_PorDefecto_EsLead()
        {
            // Las filas de campaña que ya existen siguen siendo leads (columna EsLead con DEFAULT 1).
            Assert.IsTrue(new OrigenCliente().EsLead);
            Assert.IsTrue(new LeadCampana().EsLead);
        }

        [TestMethod]
        public async Task GetOrigenes_DiceCualesSonLeadsYCualesDeCanal()
        {
            List<OrigenClienteDTO> origenes = Contenido(await ControladorDe("Carlos", null, null).GetOrigenes());

            Assert.IsTrue(origenes.Single(o => o.Codigo == META).EsLead);
            Assert.IsFalse(origenes.Single(o => o.Codigo == TIENDA_ALCOBENDAS).EsLead);
            Assert.IsFalse(origenes.Single(o => o.Codigo == "WEB").EsLead);
        }

        [TestMethod]
        public async Task GetOrigenes_UsuarioConParametro_MarcaSuOrigenPropuesto()
        {
            List<OrigenClienteDTO> origenes = Contenido(await ControladorDe("Paloma", "tienda-alcobendas ", "").GetOrigenes());

            CollectionAssert.AreEqual(new[] { TIENDA_ALCOBENDAS }, origenes.Where(o => o.PropuestoParaElUsuario).Select(o => o.Codigo).ToArray());
        }

        [TestMethod]
        public async Task GetOrigenes_SinParametro_NingunoPropuesto()
        {
            // Sin fila propia y con la fila «(defecto)» vacía: como hasta ahora, sin propuesta.
            List<OrigenClienteDTO> origenes = Contenido(await ControladorDe("Carlos", null, "").GetOrigenes());

            Assert.IsFalse(origenes.Any(o => o.PropuestoParaElUsuario));
        }

        [TestMethod]
        public async Task GetOrigenes_SinFilaPropia_UsaLaDeDefecto()
        {
            List<OrigenClienteDTO> origenes = Contenido(await ControladorDe("Carlos", null, "WEB").GetOrigenes());

            CollectionAssert.AreEqual(new[] { "WEB" }, origenes.Where(o => o.PropuestoParaElUsuario).Select(o => o.Codigo).ToArray());
        }

        [TestMethod]
        public async Task GetOrigenes_ParametroConOrigenInactivoOInexistente_NingunoPropuesto()
        {
            Assert.IsFalse(Contenido(await ControladorDe("Paloma", "FERIA-2025", null).GetOrigenes()).Any(o => o.PropuestoParaElUsuario));
            Assert.IsFalse(Contenido(await ControladorDe("Paloma", "NO-EXISTE", null).GetOrigenes()).Any(o => o.PropuestoParaElUsuario));
        }

        [TestMethod]
        public async Task GetOrigenes_FallaElParametro_DevuelveElCatalogoSinPropuesta()
        {
            CatalogoConCanales();
            ILectorParametrosUsuario lector = A.Fake<ILectorParametrosUsuario>();
            A.CallTo(() => lector.LeerParametroSinDefecto(A<string>._, A<string>._, A<string>._)).Throws(new InvalidOperationException("sin BD"));
            ClientesController controlador = Controlador();
            controlador.LectorParametrosOrigen = lector;

            List<OrigenClienteDTO> origenes = Contenido(await controlador.GetOrigenes());

            Assert.AreEqual(3, origenes.Count);
            Assert.IsFalse(origenes.Any(o => o.PropuestoParaElUsuario));
        }

        [TestMethod]
        public void PendienteDeLlamar_OrigenDeCanal_Nunca()
        {
            LeadCampana tienda = Lead("42109", new DateTime(2026, 11, 5, 10, 0, 0));
            tienda.Origen = TIENDA_ALCOBENDAS;
            tienda.EsLead = false;

            Assert.IsFalse(ReglasLeadsCampana.PendienteDeLlamar(tienda, AHORA));
        }

        [TestMethod]
        public async Task Job_OrigenDeCanal_NoAvisa()
        {
            LeadCampana tienda = Lead("42109", new DateTime(2026, 11, 3, 9, 0, 0));
            tienda.Origen = TIENDA_ALCOBENDAS;
            tienda.EsLead = false;
            LeadsSinLlamarJobsService job = Job(tienda);

            Assert.AreEqual(0, await job.AvisarAsync(AHORA));
            A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.MarcarAvisoRetraso(A<string>._, A<string>._, A<DateTime>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public void Ranking_OrigenDeCanal_NoSaleComoLead()
        {
            LeadCampana web = Lead("42109", new DateTime(2026, 11, 5, 10, 0, 0));
            web.Origen = "WEB";
            web.EsLead = false;

            List<SugerenciaContactoDTO> resultado = ImpulsoLeadsCampanaContacto.Aplicar(Lista("1001"), new[] { web }, AHORA, SinFestivos);

            CollectionAssert.AreEqual(new[] { "1001" }, resultado.Select(s => s.Cliente).ToArray());
        }

        [TestMethod]
        public void SqlLeads_SoloOrigenesQueSonLeads()
        {
            // El job y las sugerencias leen los leads con este SQL: los orígenes de canal ni se leen.
            StringAssert.Contains(RepositorioOrigenesClienteSql.SQL_LEADS, "o.EsLead = 1");
            StringAssert.Contains(RepositorioOrigenesClienteSql.SQL_CATALOGO, "EsLead");
        }

        [TestMethod]
        public async Task ImportarLeads_ConOrigenDeCanal_400()
        {
            // Los leads de Meta se importan con un origen de campaña; uno de canal no entraría en el aviso de leads
            // y además viajaría a Odoo (nesto_sync) como si fuera una campaña.
            CatalogoConCanales();
            ClientesController controlador = Controlador();

            var respuesta = await controlador.PostImportarLeads(TIENDA_ALCOBENDAS, ensayo: true) as BadRequestErrorMessageResult;

            Assert.IsNotNull(respuesta);
            StringAssert.Contains(respuesta.Message, "no es de campaña");
        }
    }
}
