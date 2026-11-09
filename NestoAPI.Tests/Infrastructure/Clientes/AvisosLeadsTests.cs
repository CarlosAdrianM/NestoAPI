using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>
    /// NestoAPI#623 (Carlos 09/11/26): aviso de lead nuevo al importarlo y, en Madrid, Toledo y Guadalajara, Alberto Sancho (ASH)
    /// y Dirección en todos (también en el recordatorio de las 24 h). Todos los datos son inventados.
    /// </summary>
    [TestClass]
    public class AvisosLeadsTests
    {
        private const string ORIGEN = "META-HARMONIA-0926";
        private static readonly DateTime AHORA = new DateTime(2026, 11, 9, 12, 0, 0);

        private IDestinatariosLeadsSinLlamar destinatarios;
        private IServicioNotificacionesPush push;
        private IRepositorioOrigenesCliente catalogo;
        private List<string> direccion;

        [TestInitialize]
        public void Preparar()
        {
            destinatarios = A.Fake<IDestinatariosLeadsSinLlamar>();
            direccion = new List<string> { "Manuel" };
            _ = A.CallTo(() => destinatarios.JefesDe("1", "JE")).Returns(new List<string> { "JV" });
            _ = A.CallTo(() => destinatarios.UsuariosDeVendedores(A<IEnumerable<string>>._))
                .ReturnsLazily((IEnumerable<string> v) => v.Select(x =>
                    x == "JE" ? "Jesus" : x == "JV" ? "JefeVentas" : x == ReglasAvisosLeads.VENDEDOR_JEFE_ZONA_CENTRO ? "Sancho" : x == "NV" ? "General" : "otro").ToList());
            _ = A.CallTo(() => destinatarios.UsuariosDireccion()).ReturnsLazily(() => direccion.ToList());
            push = A.Fake<IServicioNotificacionesPush>();
            catalogo = A.Fake<IRepositorioOrigenesCliente>();
            _ = A.CallTo(() => catalogo.LeerCatalogo()).Returns(Task.FromResult(new List<OrigenCliente>
            {
                new OrigenCliente { Codigo = ORIGEN, Descripcion = "Lead del formulario de Meta Ads (Harmonia)", Campana = "Harmonia 09/26", Activo = true, EsLead = true }
            }));
        }

        private AvisadorLeadsNuevos Avisador() => new AvisadorLeadsNuevos(destinatarios, push, catalogo);

        private static LeadNuevoAviso Aviso(string vendedor, string codigoPostal, string poblacion = "MADRID") => new LeadNuevoAviso
        {
            Empresa = "1",
            Cliente = "99001",
            Contacto = "0",
            Nombre = "ANA INVENTADA",
            Poblacion = poblacion,
            CodigoPostal = codigoPostal,
            Vendedor = vendedor,
            Origen = ORIGEN
        };

        private List<string> Buzones() => Fake.GetCalls(push)
            .Where(c => c.Method.Name == nameof(IServicioNotificacionesPush.GuardarEnBuzonDeUsuario))
            .Select(c => (string)c.Arguments[0]).OrderBy(u => u, StringComparer.Ordinal).ToList();

        private List<string> PushVendedores() => Fake.GetCalls(push)
            .Where(c => c.Method.Name == nameof(IServicioNotificacionesPush.EnviarAVendedor))
            .Select(c => (string)c.Arguments[1]).OrderBy(u => u, StringComparer.Ordinal).ToList();

        // ---------- Regla ----------

        [TestMethod]
        public void Regla_MadridToledoGuadalajara_SonZonaCentro_ElRestoNo()
        {
            Assert.IsTrue(ReglasAvisosLeads.EsZonaConJefeYDireccion("28001"));
            Assert.IsTrue(ReglasAvisosLeads.EsZonaConJefeYDireccion("45600"));
            Assert.IsTrue(ReglasAvisosLeads.EsZonaConJefeYDireccion(" 19200 "));
            Assert.IsFalse(ReglasAvisosLeads.EsZonaConJefeYDireccion("08001"));
            Assert.IsFalse(ReglasAvisosLeads.EsZonaConJefeYDireccion("8850"));
            Assert.IsFalse(ReglasAvisosLeads.EsZonaConJefeYDireccion(null));
            Assert.IsFalse(ReglasAvisosLeads.EsZonaConJefeYDireccion("2"));
        }

        // ---------- Aviso de lead nuevo ----------

        [TestMethod]
        public async Task LeadNuevo_Madrid_AvisaAlVendedorAAlbertoYADireccion()
        {
            await Avisador().Avisar(Aviso("JE", "28001"));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Jesus", "NUEVAVISION\\Manuel", "NUEVAVISION\\Sancho" }, Buzones());
            CollectionAssert.AreEqual(new[] { "ASH", "JE" }, PushVendedores());
            A.CallTo(() => destinatarios.JefesDe(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeadNuevo_Madrid_AlbertoEsElVendedorYEstaEnDireccion_SinRepetir()
        {
            direccion = new List<string> { "Manuel", "NUEVAVISION\\Sancho" };

            await Avisador().Avisar(Aviso("ASH", "28001"));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Manuel", "NUEVAVISION\\Sancho" }, Buzones());
            CollectionAssert.AreEqual(new[] { "ASH" }, PushVendedores());
        }

        [TestMethod]
        public async Task LeadNuevo_Barcelona_SoloAlVendedor()
        {
            await Avisador().Avisar(Aviso("JE", "08001", "BARCELONA"));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\Jesus" }, Buzones());
            CollectionAssert.AreEqual(new[] { "JE" }, PushVendedores());
            A.CallTo(() => destinatarios.UsuariosDireccion()).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeadNuevo_VendedorGeneralEnGuadalajara_AlbertoDireccionYLosDelGeneral()
        {
            await Avisador().Avisar(Aviso(Constantes.Vendedores.VENDEDOR_GENERAL, "19001", "GUADALAJARA"));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\General", "NUEVAVISION\\Manuel", "NUEVAVISION\\Sancho" }, Buzones());
            CollectionAssert.AreEqual(new[] { "ASH", "NV" }, PushVendedores());
        }

        [TestMethod]
        public async Task LeadNuevo_Texto_ConLaCampanaElNombreLaPoblacionYElCliente()
        {
            NotificacionPushDTO enviada = null;
            _ = A.CallTo(() => push.GuardarEnBuzonDeUsuario(A<string>._, A<string>._, A<NotificacionPushDTO>._))
                .Invokes((string u, string app, NotificacionPushDTO n) => enviada = n);

            await Avisador().Avisar(Aviso("JE", "08001", "BARCELONA"));

            Assert.AreEqual("Nuevo lead de Harmonia 09/26", enviada.Titulo);
            Assert.AreEqual("Nuevo lead de Harmonia 09/26: ANA INVENTADA (BARCELONA), cliente 99001. Llámele hoy.", enviada.Cuerpo);
            Assert.AreEqual(AvisadorLeadsNuevos.TIPO_NOTIFICACION, enviada.Tipo);
            Assert.AreEqual("LeadNuevo", enviada.Datos["tipo"]);
            Assert.AreEqual("1", enviada.Datos["empresa"]);
            Assert.AreEqual("99001", enviada.Datos["cliente"]);
            Assert.AreEqual("0", enviada.Datos["contacto"]);
            Assert.AreEqual(ORIGEN, enviada.Datos["origen"]);
            A.CallTo(() => push.GuardarEnBuzonDeUsuario("NUEVAVISION\\Jesus", Constantes.Aplicaciones.NESTO, A<NotificacionPushDTO>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public void LeadNuevo_SinPoblacion_TextoSinParentesis()
        {
            Assert.AreEqual("Nuevo lead de META-OTRA: ANA INVENTADA, cliente 99001. Llámele hoy.",
                AvisadorLeadsNuevos.Texto(Aviso("JE", null, null), "META-OTRA"));
        }

        // ---------- El importador avisa ----------

        private IRepositorioLeadsMeta repositorio;
        private IAltaClientesLeads alta;
        private IAvisadorLeadsNuevos avisador;
        private List<Exception> errores;

        private ImportadorLeadsMeta Importador()
        {
            repositorio = A.Fake<IRepositorioLeadsMeta>();
            alta = A.Fake<IAltaClientesLeads>();
            avisador = A.Fake<IAvisadorLeadsNuevos>();
            errores = new List<Exception>();
            _ = A.CallTo(() => repositorio.ClienteDelLead(A<string>._, A<string>._)).Returns(Task.FromResult<string>(null));
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult<ClienteParecidoLead>(null));
            _ = A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<bool>._, A<string>._))
                .Returns(Task.FromResult(ResultadoOrigenLead.Puesto));
            _ = A.CallTo(() => alta.DatosCodigoPostal(A<string>._)).Returns(Task.FromResult(new RespuestaDatosGeneralesClientes
            {
                CodigoPostal = "28001", Poblacion = "MADRID", Provincia = "MADRID", Ruta = "AT", VendedorEstetica = "ZZ"
            }));
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Returns(Task.FromResult("99001 "));
            return new ImportadorLeadsMeta(repositorio, alta, errores.Add, () => AHORA, avisador);
        }

        private static LeadMeta Lead() => new LeadMeta
        {
            Fila = 2,
            Id = "2000000000000001",
            FechaCreacion = new DateTime(2026, 11, 9, 10, 0, 0),
            Plataforma = "ig",
            Nombre = "Ana Inventada",
            Email = "ana@ejemplo.com",
            Telefono = "600111222",
            TelefonoParaBuscar = "600111222",
            CodigoPostal = "28001"
        };

        [TestMethod]
        public async Task Importador_LeadCreado_AvisaConLosDatosDelAlta()
        {
            ImportadorLeadsMeta importador = Importador();

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREADO, r.Leads[0].Codigo);
            A.CallTo(() => avisador.Avisar(A<LeadNuevoAviso>.That.Matches(a => a.Empresa == "1" && a.Cliente == "99001" && a.Contacto == "0"
                && a.Nombre == "ANA INVENTADA" && a.Poblacion == "MADRID" && a.CodigoPostal == "28001" && a.Vendedor == "ZZ" && a.Origen == ORIGEN)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Importador_Ensayo_NoAvisa()
        {
            ImportadorLeadsMeta importador = Importador();

            _ = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: true, "u");

            A.CallTo(() => avisador.Avisar(A<LeadNuevoAviso>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Importador_Ensayo_ClienteQueYaExistia_NoAvisa()
        {
            ImportadorLeadsMeta importador = Importador();
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new ClienteParecidoLead { Cliente = "15000", Vendedor = "XX", CodigoPostal = "28001" }));

            _ = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: true, "u");

            A.CallTo(() => avisador.Avisar(A<LeadNuevoAviso>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Importador_ClienteQueYaExistiaSinOrigen_AvisaConSuFicha()
        {
            ImportadorLeadsMeta importador = Importador();
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult(new ClienteParecidoLead
            {
                Cliente = "15000", Contacto = "1  ", Vendedor = "XX", Nombre = "CENTRO INVENTADO ", Poblacion = "TOLEDO", CodigoPostal = "45001"
            }));

            _ = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            A.CallTo(() => avisador.Avisar(A<LeadNuevoAviso>.That.Matches(a => a.Cliente == "15000" && a.Contacto == "1" && a.Vendedor == "XX"
                && a.Nombre == "CENTRO INVENTADO" && a.Poblacion == "TOLEDO" && a.CodigoPostal == "45001" && a.Origen == ORIGEN)))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Importador_ClienteQueYaTeniaEsteOrigen_NoAvisaOtraVez()
        {
            ImportadorLeadsMeta importador = Importador();
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new ClienteParecidoLead { Cliente = "15000", Origen = ORIGEN, Vendedor = "XX" }));
            _ = A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<bool>._, A<string>._))
                .Returns(Task.FromResult(ResultadoOrigenLead.YaLoTenia));

            _ = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            A.CallTo(() => avisador.Avisar(A<LeadNuevoAviso>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Importador_FallaElAviso_ElLeadQuedaCreadoYVaAElmah()
        {
            ImportadorLeadsMeta importador = Importador();
            _ = A.CallTo(() => avisador.Avisar(A<LeadNuevoAviso>._)).ThrowsAsync(new InvalidOperationException("sin conexión"));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREADO, r.Leads[0].Codigo);
            Assert.AreEqual(1, r.Creados);
            Assert.AreEqual(0, r.Errores);
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "2000000000000001");
            A.CallTo(() => alta.PublicarConOrigen(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<bool>._, A<string>._)).MustHaveHappenedOnceExactly();
        }

        // ---------- Recordatorio de las 24 h ----------

        private LeadsSinLlamarJobsService Job(LeadCampana lead)
        {
            _ = A.CallTo(() => catalogo.LeerLeads(null)).Returns(Task.FromResult(new List<LeadCampana> { lead }));
            return new LeadsSinLlamarJobsService(catalogo, destinatarios, push, d => d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday);
        }

        private static LeadCampana LeadSinLlamar(string codigoPostal) => new LeadCampana
        {
            Empresa = "1",
            Cliente = "42109",
            Contacto = "0",
            Nombre = "Lead 42109",
            CodigoPostal = codigoPostal,
            Vendedor = "JE",
            Origen = ORIGEN,
            FechaOrigen = new DateTime(2026, 11, 5, 9, 0, 0),
            HorasParaLlamar = 24
        };

        [TestMethod]
        public async Task Job24h_Toledo_VendedorJefeAlbertoYDireccion()
        {
            Assert.AreEqual(1, await Job(LeadSinLlamar("45001")).AvisarAsync(AHORA));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\JefeVentas", "NUEVAVISION\\Jesus", "NUEVAVISION\\Manuel", "NUEVAVISION\\Sancho" }, Buzones());
            CollectionAssert.AreEqual(new[] { "ASH", "JE", "JV" }, PushVendedores());
        }

        [TestMethod]
        public async Task Job24h_FueraDeLaZona_SinCambios()
        {
            Assert.AreEqual(1, await Job(LeadSinLlamar("08001")).AvisarAsync(AHORA));

            CollectionAssert.AreEqual(new[] { "NUEVAVISION\\JefeVentas", "NUEVAVISION\\Jesus" }, Buzones());
            CollectionAssert.AreEqual(new[] { "JE", "JV" }, PushVendedores());
            A.CallTo(() => destinatarios.UsuariosDireccion()).MustNotHaveHappened();
        }
    }
}
