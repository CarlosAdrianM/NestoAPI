using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Clientes;
using NestoAPI.Infraestructure.Clientes.Origenes;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.Sincronizacion;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Clientes
{
    /// <summary>NestoAPI#623 corte 4: importar el fichero de leads de Meta. Todos los datos son inventados.</summary>
    [TestClass]
    public class ImportadorLeadsMetaTests
    {
        private const string ORIGEN = "META-HARMONIA-0926";

        private const string CABECERA = "id\tcreated_time\tad_id\tad_name\tadset_id\tadset_name\tcampaign_id\tcampaign_name\tform_id\tform_name\tis_organic\tplatform\t"
            + "¿cuál_es_tu_situación_profesional?_\t¿qué_te_gustaría_solicitar?\tfull_name\temail\tphone_number\tpost_code\t"
            + "deseo_recibir_por_correo_electrónico_novedades,_formación,_información_sobre_productos_y_promociones_de_nueva_visión.\tlead_status";

        private static string FilaCsv(string id, string fecha, string plataforma, string nombre, string email, string telefono, string cp, string consentimiento) =>
            $"{id}\t{fecha}\t1\tAnuncio\t2\tConjunto\t3\tCampaña\t4\tFormulario\tfalse\t{plataforma}\ttengo_un_centro_de_estética_\tuna_demostración_de_harmonia\t"
            + $"{nombre}\t{email}\t{telefono}\t{cp}\t{consentimiento}\tcomplete";

        private static string Xml(params string[][] filas)
        {
            var sb = new StringBuilder();
            _ = sb.Append("<?xml version=\"1.0\"?>\r\n<?mso-application progid=\"Excel.Sheet\"?>\r\n");
            _ = sb.Append("<Workbook xmlns=\"urn:schemas-microsoft-com:office:spreadsheet\" xmlns:ss=\"urn:schemas-microsoft-com:office:spreadsheet\">\r\n");
            _ = sb.Append(" <Worksheet ss:Name=\"Leads\"><Table>\r\n");
            foreach (string[] fila in filas)
            {
                _ = sb.Append("  <Row>");
                foreach (string celda in fila)
                {
                    _ = sb.Append("<Cell ss:StyleID=\"s62\"><Data ss:Type=\"String\">").Append(System.Security.SecurityElement.Escape(celda)).Append("</Data></Cell>");
                }
                _ = sb.Append("</Row>\r\n");
            }
            _ = sb.Append(" </Table></Worksheet>\r\n</Workbook>");
            return sb.ToString();
        }

        private static string[] Celdas(string filaCsv) => filaCsv.Split('\t');

        // ---------- Lector ----------

        [TestMethod]
        public void Lector_Xml_LeeLosCamposDelLead()
        {
            string xml = Xml(Celdas(CABECERA), Celdas(FilaCsv("1000000000000001", "2026-11-05T20:48:42.000", "ig", "Ana Prueba Inventada",
                "Ana.Prueba@Ejemplo.com", "+34600111222", "28001", "false")));

            List<LeadMeta> leads = LectorLeadsMeta.Leer(Encoding.UTF8.GetBytes(xml));

            Assert.AreEqual(1, leads.Count);
            LeadMeta lead = leads[0];
            Assert.AreEqual("1000000000000001", lead.Id);
            Assert.AreEqual(2, lead.Fila);
            Assert.AreEqual("ig", lead.Plataforma);
            Assert.AreEqual("Ana Prueba Inventada", lead.Nombre);
            Assert.AreEqual("ana.prueba@ejemplo.com", lead.Email);
            Assert.AreEqual("600111222", lead.Telefono);
            Assert.AreEqual("600111222", lead.TelefonoParaBuscar);
            Assert.AreEqual("28001", lead.CodigoPostal);
            Assert.IsFalse(lead.AceptaComunicaciones);
            Assert.AreEqual("tengo un centro de estética", lead.SituacionProfesional);
            Assert.AreEqual("una demostración de harmonia", lead.Solicita);
            // El Excel XML de Meta trae la hora SIN zona y ya es la de España: no se convierte (antes sumaba una hora).
            Assert.AreEqual(new DateTime(2026, 11, 5, 20, 48, 42), lead.FechaCreacion);
        }

        // ---------- Hora del lead (regresión: los leads del XML del 09/11/26 quedaron con +1 h) ----------

        [TestMethod]
        public void FechaEspana_SinZonaComoElXmlDeMeta_SeQuedaTalCual()
        {
            Assert.AreEqual(new DateTime(2026, 11, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-11-05T13:49:14.000"));
            Assert.AreEqual(new DateTime(2026, 7, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-07-05T13:49:14.000"));
        }

        [TestMethod]
        public void FechaEspana_Invierno_ConZonaSeConvierteAEspana()
        {
            // 05/11 = horario de invierno (UTC+1).
            Assert.AreEqual(new DateTime(2026, 11, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-11-05T12:49:14+0000"));
            Assert.AreEqual(new DateTime(2026, 11, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-11-05T13:49:14+0100"));
            Assert.AreEqual(new DateTime(2026, 11, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-11-05T13:49:14+01:00"));
            Assert.AreEqual(new DateTime(2026, 11, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-11-05T12:49:14Z"));
        }

        [TestMethod]
        public void FechaEspana_Verano_ConZonaSeConvierteAEspana()
        {
            // 05/07 = horario de verano (UTC+2).
            Assert.AreEqual(new DateTime(2026, 7, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-07-05T11:49:14+0000"));
            Assert.AreEqual(new DateTime(2026, 7, 5, 13, 49, 14), LectorLeadsMeta.FechaEspana("2026-07-05T13:49:14+02:00"));
        }

        [TestMethod]
        public void FechaEspana_TextoQueNoEsFecha_Null()
        {
            Assert.IsNull(LectorLeadsMeta.FechaEspana("ayer"));
            Assert.IsNull(LectorLeadsMeta.FechaEspana(" "));
        }

        [TestMethod]
        public void Lector_XmlEnWindows1252ConCabecerasConTildes_ReconoceLasColumnas()
        {
            string xml = Xml(Celdas(CABECERA), Celdas(FilaCsv("1000000000000002", "2026-11-05T12:00:00.000", "fb", "Bea Inventada",
                "bea@ejemplo.com", "+34600333444", "8850", "true")));
            // Meta lo declara UTF-8 pero las tildes llegan en Windows-1252.
            byte[] bytes = Encoding.GetEncoding(1252).GetBytes(xml);

            LeadMeta lead = LectorLeadsMeta.Leer(bytes).Single();

            Assert.IsTrue(lead.AceptaComunicaciones);
            Assert.AreEqual("tengo un centro de estética", lead.SituacionProfesional);
            Assert.AreEqual("08850", lead.CodigoPostal, "Excel se come el cero de delante");
        }

        [TestMethod]
        public void Lector_CabecerasEstropeadas_SeReconocenPorFragmento()
        {
            string cabecera = CABECERA.Replace("situación", "situaciÃ³n").Replace("¿qué_te_gustaría_solicitar?", "Â¿quÃ©_te_gustarÃ­a_solicitar?")
                .Replace("deseo_recibir_por_correo_electrónico", "deseo_recibir_por_correo_electrÃ³nico");
            string csv = cabecera + "\r\n" + FilaCsv("1000000000000003", "2026-11-05T12:00:00.000", "fb", "Carla Inventada", "c@ejemplo.com", "+34600555666", "28100", "true");

            LeadMeta lead = LectorLeadsMeta.Leer(csv).Single();

            Assert.IsTrue(lead.AceptaComunicaciones);
            Assert.IsNotNull(lead.SituacionProfesional);
            Assert.IsNotNull(lead.Solicita);
        }

        [TestMethod]
        public void Lector_CsvUtf16ConTabuladores()
        {
            string csv = CABECERA + "\r\n" + FilaCsv("1000000000000004", "2026-11-06T00:25:57+0000", "fb", "Dora Inventada", "d@ejemplo.com", "p:+34600777888", "28002", "false") + "\r\n";
            byte[] bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(csv)).ToArray();

            LeadMeta lead = LectorLeadsMeta.Leer(bytes).Single();

            Assert.AreEqual("1000000000000004", lead.Id);
            Assert.AreEqual("600777888", lead.Telefono);
            Assert.AreEqual(new DateTime(2026, 11, 6, 1, 25, 57), lead.FechaCreacion);
        }

        [TestMethod]
        public void Lector_CsvUtf8ConComasYComillas()
        {
            string cabecera = "id,created_time,platform,full_name,email,phone_number,post_code,\"deseo_recibir_por_correo_electrónico_novedades,_formación\"";
            string csv = cabecera + "\n1000000000000005,2026-11-05T09:00:00.000,ig,\"Pérez, Eva\",e@ejemplo.com,+34600999000,28003,true\n";

            LeadMeta lead = LectorLeadsMeta.Leer(Encoding.UTF8.GetBytes(csv)).Single();

            Assert.AreEqual("Pérez, Eva", lead.Nombre);
            Assert.IsTrue(lead.AceptaComunicaciones);
            Assert.AreEqual("28003", lead.CodigoPostal);
        }

        [TestMethod]
        public void Lector_SinColumnaId_LanzaFormatException()
        {
            _ = Assert.ThrowsException<FormatException>(() => LectorLeadsMeta.Leer("nombre,telefono\nX,600000000"));
        }

        [TestMethod]
        public void NormalizarTelefono_Extranjero_ConservaTodoYBuscaPorLas9Ultimas()
        {
            (string ficha, string buscar) = LectorLeadsMeta.NormalizarTelefono("+351 912 345 678");
            Assert.AreEqual("351912345678", ficha);
            Assert.AreEqual("912345678", buscar);
        }

        // ---------- Importador ----------

        private IRepositorioLeadsMeta repositorio;
        private IAltaClientesLeads alta;
        private List<Exception> errores;
        private ImportadorLeadsMeta importador;

        [TestInitialize]
        public void Preparar()
        {
            repositorio = A.Fake<IRepositorioLeadsMeta>();
            alta = A.Fake<IAltaClientesLeads>();
            errores = new List<Exception>();
            _ = A.CallTo(() => repositorio.ClienteDelLead(A<string>._, A<string>._)).Returns(Task.FromResult<string>(null));
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._)).Returns(Task.FromResult<ClienteParecidoLead>(null));
            _ = A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<bool>._, A<string>._))
                .Returns(Task.FromResult(ResultadoOrigenLead.Puesto));
            _ = A.CallTo(() => alta.DatosCodigoPostal(A<string>._)).Returns(Task.FromResult(new RespuestaDatosGeneralesClientes
            {
                CodigoPostal = "28001", Poblacion = "MADRID", Provincia = "MADRID", Ruta = "AT", VendedorEstetica = "ZZ"
            }));
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Returns(Task.FromResult("99001"));
            importador = new ImportadorLeadsMeta(repositorio, alta, errores.Add, () => new DateTime(2026, 11, 9, 12, 0, 0));
        }

        private static LeadMeta Lead(string id = "2000000000000001", string telefono = "600111222", string email = "ana@ejemplo.com", bool acepta = false) => new LeadMeta
        {
            Fila = 2,
            Id = id,
            FechaCreacion = new DateTime(2026, 11, 5, 21, 48, 0),
            Plataforma = "ig",
            SituacionProfesional = "tengo un centro de estética",
            Solicita = "una demostración",
            Nombre = "Ana Inventada",
            Email = email,
            Telefono = telefono,
            TelefonoParaBuscar = telefono,
            CodigoPostal = "28001",
            AceptaComunicaciones = acepta
        };

        [TestMethod]
        public async Task Ensayo_LeadNuevo_DiceQueSeCrearaSinEscribir()
        {
            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: true, "NUEVAVISION\\Prueba");

            Assert.AreEqual(1, r.Creados);
            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREAR, r.Leads[0].Codigo);
            Assert.AreEqual("ZZ", r.Leads[0].Vendedor);
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<bool>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.QuitarBoletin(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => alta.PublicarConOrigen(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Real_LeadNuevo_CreaElClienteComoAltaSinNifConElVendedorDelCpYElOrigenConLaFechaDelLead()
        {
            ClienteCrear creado = null;
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Invokes((ClienteCrear c) => creado = c).Returns(Task.FromResult("99001"));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "NUEVAVISION\\Prueba");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREADO, r.Leads[0].Codigo);
            Assert.AreEqual("99001", r.Leads[0].Cliente);
            Assert.AreEqual("ANA INVENTADA", creado.Nombre);
            Assert.IsNull(creado.Nif);
            Assert.AreEqual(Constantes.Clientes.Estados.PRIMERA_VISITA, creado.Estado);
            Assert.AreEqual("ZZ", creado.VendedorEstetica);
            Assert.AreEqual("AT", creado.Ruta);
            Assert.AreEqual("MADRID", creado.Poblacion);
            Assert.AreEqual("600111222", creado.Telefono);
            Assert.AreEqual("ana@ejemplo.com", creado.PersonasContacto.Single().CorreoElectronico);
            Assert.IsNull(creado.Origen, "el origen se pone después, con la fecha del lead");
            StringAssert.StartsWith(creado.Comentarios, "Lead de Meta (Instagram) 05/11 21:48: tengo un centro de estética; solicita: una demostración.");
            A.CallTo(() => repositorio.PonerOrigenLead("1", "99001", ORIGEN, new DateTime(2026, 11, 5, 21, 48, 0), "2000000000000001", false, "NUEVAVISION\\Prueba"))
                .MustHaveHappenedOnceExactly();
            A.CallTo(() => alta.PublicarConOrigen("1", "99001", ORIGEN, new DateTime(2026, 11, 5, 21, 48, 0), false, "NUEVAVISION\\Prueba"))
                .MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Real_SinConsentimiento_QuitaElBoletinYLoDiceEnElComentario()
        {
            ClienteCrear creado = null;
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Invokes((ClienteCrear c) => creado = c).Returns(Task.FromResult("99001"));

            _ = await importador.Importar(new[] { Lead(acepta: false) }, ORIGEN, ensayo: false, "u");

            StringAssert.EndsWith(creado.Comentarios, ImportadorLeadsMeta.TEXTO_SIN_CONSENTIMIENTO);
            A.CallTo(() => repositorio.QuitarBoletin("1", "99001")).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task Real_ConConsentimiento_NoTocaElBoletin()
        {
            ClienteCrear creado = null;
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Invokes((ClienteCrear c) => creado = c).Returns(Task.FromResult("99001"));

            _ = await importador.Importar(new[] { Lead(acepta: true) }, ORIGEN, ensayo: false, "u");

            Assert.IsFalse(creado.Comentarios.Contains(ImportadorLeadsMeta.TEXTO_SIN_CONSENTIMIENTO));
            A.CallTo(() => repositorio.QuitarBoletin(A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, true, A<string>._)).MustHaveHappenedOnceExactly();
        }

        [TestMethod]
        public async Task ClienteConElMismoTelefonoSinOrigen_NoSeCreaYSeLePoneElOrigen()
        {
            _ = A.CallTo(() => repositorio.BuscarParecido("1", "600111222", "ana@ejemplo.com"))
                .Returns(Task.FromResult(new ClienteParecidoLead { Cliente = "15000", Vendedor = "XX" }));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(1, r.YaExistian);
            Assert.AreEqual("15000", r.Leads[0].Cliente);
            Assert.AreEqual("Ya existía: se le ha puesto el origen.", r.Leads[0].Motivo);
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustNotHaveHappened();
            A.CallTo(() => repositorio.PonerOrigenLead("1", "15000", ORIGEN, A<DateTime>._, A<string>._, A<bool>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => alta.PublicarConOrigen("1", "15000", ORIGEN, A<DateTime>._, A<bool>._, A<string>._)).MustHaveHappenedOnceExactly();
            A.CallTo(() => repositorio.QuitarBoletin(A<string>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task ClienteConOtroOrigen_NoSePisaElOrigen()
        {
            _ = A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._))
                .Returns(Task.FromResult(new ClienteParecidoLead { Cliente = "15000", Origen = "FERIA-2026" }));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_YA_EXISTIA, r.Leads[0].Codigo);
            StringAssert.Contains(r.Leads[0].Motivo, "FERIA-2026");
            A.CallTo(() => repositorio.PonerOrigenLead(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<string>._, A<bool>._, A<string>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task LeadYaImportado_NoHaceNada()
        {
            _ = A.CallTo(() => repositorio.ClienteDelLead("1", "2000000000000001")).Returns(Task.FromResult("99001"));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_YA_EXISTIA, r.Leads[0].Codigo);
            Assert.AreEqual("99001", r.Leads[0].Cliente);
            A.CallTo(() => repositorio.BuscarParecido(A<string>._, A<string>._, A<string>._)).MustNotHaveHappened();
            A.CallTo(() => alta.Crear(A<ClienteCrear>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task RepetidoEnElFichero_EnEnsayoSoloSeCreaUnaVez()
        {
            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead(), Lead(id: "2000000000000009", email: null) }, ORIGEN, ensayo: true, "u");

            Assert.AreEqual(1, r.Creados);
            Assert.AreEqual(1, r.YaExistian);
            StringAssert.Contains(r.Leads[1].Motivo, "Repetido en el fichero");
        }

        [TestMethod]
        public async Task UnLeadQueFalla_NoParaALosDemasYElmahSoloLlevaElIdDelLead()
        {
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>.That.Matches(c => c.Telefono == "600111222")))
                .Throws(new System.ComponentModel.DataAnnotations.ValidationException("Ya existe un cliente con ese CIF/NIF"));
            LeadMeta otro = Lead(id: "2000000000000002", telefono: "600999888", email: "otra@ejemplo.com");

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead(), otro }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(1, r.Errores);
            Assert.AreEqual(1, r.Creados);
            StringAssert.Contains(r.Leads[0].Motivo, "Ya existe un cliente");
            Assert.AreEqual(1, errores.Count);
            StringAssert.Contains(errores[0].Message, "2000000000000001");
            Assert.IsFalse(errores[0].Message.Contains("Ana"));
            Assert.IsFalse(errores[0].Message.Contains("600111222"));
            Assert.IsFalse(errores[0].Message.Contains("ana@ejemplo.com"));
        }

        [TestMethod]
        public void NormalizarCodigoPostal_CuatroCifras_SeRellenaConUnCero()
        {
            Assert.AreEqual("03330", LectorLeadsMeta.NormalizarCodigoPostal("3330"));
            Assert.AreEqual("28001", LectorLeadsMeta.NormalizarCodigoPostal("28001"));
            Assert.AreEqual("28001", LectorLeadsMeta.NormalizarCodigoPostal(" 28 001 "));
            Assert.IsTrue(LectorLeadsMeta.EsCodigoPostalValido("03330"));
            Assert.IsFalse(LectorLeadsMeta.EsCodigoPostalValido(LectorLeadsMeta.NormalizarCodigoPostal("123")));
            Assert.IsFalse(LectorLeadsMeta.EsCodigoPostalValido(LectorLeadsMeta.NormalizarCodigoPostal("1234567")));
        }

        [TestMethod]
        public async Task CodigoPostalDeCincoCifras_SeBuscaSuVendedor()
        {
            LeadMeta lead = Lead();
            lead.CodigoPostal = LectorLeadsMeta.NormalizarCodigoPostal("3330");

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { lead }, ORIGEN, ensayo: true, "u");

            A.CallTo(() => alta.DatosCodigoPostal("03330")).MustHaveHappenedOnceExactly();
            Assert.AreEqual("ZZ", r.Leads[0].Vendedor);
            Assert.IsNull(r.Leads[0].Motivo);
        }

        [TestMethod]
        public async Task CodigoPostalQueNoEsDeCincoCifras_SeAvisaYSeCreaSinCpAlVendedorGeneral()
        {
            ClienteCrear creado = null;
            _ = A.CallTo(() => alta.Crear(A<ClienteCrear>._)).Invokes((ClienteCrear c) => creado = c).Returns(Task.FromResult("99001"));
            LeadMeta lead = Lead();
            lead.CodigoPostal = LectorLeadsMeta.NormalizarCodigoPostal("123");

            ResultadoImportacionLeadsDTO ensayo = await importador.Importar(new[] { lead }, ORIGEN, ensayo: true, "u");
            ResultadoImportacionLeadsDTO real = await importador.Importar(new[] { lead }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREAR, ensayo.Leads[0].Codigo);
            StringAssert.Contains(ensayo.Leads[0].Motivo, "no es válido");
            Assert.AreEqual(Constantes.Vendedores.VENDEDOR_GENERAL, ensayo.Leads[0].Vendedor);
            A.CallTo(() => alta.DatosCodigoPostal(A<string>._)).MustNotHaveHappened();
            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREADO, real.Leads[0].Codigo);
            Assert.IsNull(creado.CodigoPostal);
            Assert.AreEqual(Constantes.Vendedores.VENDEDOR_GENERAL, creado.VendedorEstetica);
        }

        [TestMethod]
        public async Task CodigoPostalQueNoExiste_SeAvisaYSeCreaSinCp()
        {
            _ = A.CallTo(() => alta.DatosCodigoPostal("28001")).Throws(new CodigoPostalNoExisteException("No existe el código postal 28001."));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: true, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREAR, r.Leads[0].Codigo);
            StringAssert.Contains(r.Leads[0].Motivo, "no existe");
        }

        [TestMethod]
        public async Task SinTelefonoNiCorreo_Error()
        {
            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead(telefono: null, email: null) }, ORIGEN, ensayo: true, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_ERROR, r.Leads[0].Codigo);
        }

        [TestMethod]
        public async Task SiFallaLaPublicacionAOdoo_ElClienteQuedaCreadoYSeDice()
        {
            _ = A.CallTo(() => alta.PublicarConOrigen(A<string>._, A<string>._, A<string>._, A<DateTime>._, A<bool>._, A<string>._))
                .Throws(new InvalidOperationException("pubsub"));

            ResultadoImportacionLeadsDTO r = await importador.Importar(new[] { Lead() }, ORIGEN, ensayo: false, "u");

            Assert.AreEqual(ImportadorLeadsMeta.CODIGO_CREADO, r.Leads[0].Codigo);
            StringAssert.Contains(r.Leads[0].Motivo, "Odoo");
            Assert.AreEqual(1, errores.Count);
        }

        [TestMethod]
        public void MensajeSync_SinOrigen_NoSerializaLosCamposNuevos()
        {
            string json = System.Text.Json.JsonSerializer.Serialize(new ClienteSyncMessage { Cliente = "1" });
            Assert.IsFalse(json.Contains("\"Origen\""));
            Assert.IsFalse(json.Contains("AceptaComunicaciones"));

            string conOrigen = System.Text.Json.JsonSerializer.Serialize(new ClienteSyncMessage { Cliente = "1", Origen = ORIGEN, FechaOrigen = new DateTime(2026, 11, 5), AceptaComunicaciones = false });
            StringAssert.Contains(conOrigen, "\"Origen\":\"META-HARMONIA-0926\"");
            StringAssert.Contains(conOrigen, "\"AceptaComunicaciones\":false");
        }
    }
}
