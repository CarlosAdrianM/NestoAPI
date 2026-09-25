using FakeItEasy;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure;
using NestoAPI.Infraestructure.Cobros;
using NestoAPI.Models;
using NestoAPI.Models.Cobros;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure
{
    /// <summary>
    /// NestoAPI#534 (corte 1): el job nace APAGADO y, en modo sombra, solo escribe a administración.
    /// NestoAPI#544 (corte 2): un aviso por cliente con todas sus facturas, saludo por hora y nombre
    /// de pila, bloque para copiar y pegar, memoria (solo en Activo) y resumen a administración con
    /// los apuntes negativos.
    /// </summary>
    [TestClass]
    public class AvisosFacturasVencidasJobsServiceTests
    {
        private static readonly DateTime AHORA = new DateTime(2026, 9, 24, 7, 45, 0);
        private static readonly DateTime HOY = AHORA.Date;
        private const string IBAN = "ES06 2100 6273 9002 0006 3554";

        private ILectorParametrosUsuario lector;
        private IServicioCorreoElectronico correo;
        private IAlmacenAvisosFacturasVencidas almacen;
        private List<MailMessage> enviados;
        private List<AvisoFacturaVencidaRegistrado> registrados;
        private List<ApunteNegativoClienteDTO> negativos;
        private Dictionary<string, string> saludosOpenAI;
        private bool openAIFalla;
        private int vecesCalculado;
        private int diasPedidos;
        private List<string> pdfsPedidos;

        [TestInitialize]
        public void Setup()
        {
            lector = A.Fake<ILectorParametrosUsuario>();
            correo = A.Fake<IServicioCorreoElectronico>();
            almacen = A.Fake<IAlmacenAvisosFacturasVencidas>();
            enviados = new List<MailMessage>();
            registrados = new List<AvisoFacturaVencidaRegistrado>();
            negativos = new List<ApunteNegativoClienteDTO>();
            saludosOpenAI = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            openAIFalla = false;
            pdfsPedidos = new List<string>();
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._))
                .Invokes((MailMessage m) => enviados.Add(Copiar(m)))
                .Returns(true);
            A.CallTo(() => almacen.Registrar(A<IEnumerable<AvisoFacturaVencidaRegistrado>>._))
                .Invokes((IEnumerable<AvisoFacturaVencidaRegistrado> filas) => registrados.AddRange(filas))
                .Returns(Task.CompletedTask);
            vecesCalculado = 0;
            diasPedidos = 0;
        }

        // El MailMessage se libera (using) tras mandarlo: nos quedamos una copia de lo que importa
        private static MailMessage Copiar(MailMessage m)
        {
            MailMessage copia = new MailMessage
            {
                From = m.From,
                Subject = m.Subject,
                Body = m.Body,
                IsBodyHtml = m.IsBodyHtml
            };
            foreach (MailAddress a in m.To) copia.To.Add(a);
            foreach (MailAddress a in m.CC) copia.CC.Add(a);
            foreach (MailAddress a in m.Bcc) copia.Bcc.Add(a);
            foreach (MailAddress a in m.ReplyToList) copia.ReplyToList.Add(a);
            foreach (Attachment at in m.Attachments) copia.Attachments.Add(new Attachment(new System.IO.MemoryStream(new byte[] { 1 }), at.Name));
            return copia;
        }

        private void Parametro(string clave, string valor)
            => A.CallTo(() => lector.LeerParametro("1", "(defecto)", clave)).Returns(valor);

        private Task<List<AvisoFacturaVencidaDTO>> Calcular(int dias, DateTime hoy, List<AvisoFacturaVencidaDTO> resultado)
        {
            vecesCalculado++;
            diasPedidos = dias;
            return Task.FromResult(resultado);
        }

        private static AvisoFacturaVencidaDTO Aviso(string factura = "NV2612000", string motivo = null,
            string destinatarios = "cobros@ana.es", string nombre = "PELUQUERÍA ANA", string cliente = "15191",
            int nOrden = 1, decimal importe = 1234.5m, DateTime? vencimiento = null, string personaContacto = null,
            bool tocaHoy = true, int numeroAviso = 1)
            => new AvisoFacturaVencidaDTO
            {
                NOrden = nOrden,
                Cliente = cliente,
                Contacto = "0",
                Nombre = nombre,
                Factura = factura,
                FechaFactura = new DateTime(2026, 8, 14),
                Vencimiento = vencimiento ?? new DateTime(2026, 9, 13),
                Importe = importe,
                DiasVencida = 11,
                Destinatarios = destinatarios,
                NombrePersonaContacto = personaContacto,
                Motivo = motivo,
                TocaHoy = tocaHoy,
                NumeroAviso = numeroAviso
            };

        private DependenciasAvisosFacturasVencidas Deps(List<AvisoFacturaVencidaDTO> resultado)
            => new DependenciasAvisosFacturasVencidas
            {
                Lector = lector,
                Correo = correo,
                Almacen = almacen,
                CalcularCandidatos = (d, h) => Calcular(d, h, resultado),
                LeerApuntesNegativos = clientes => Task.FromResult(negativos.Where(n => clientes.Contains(n.Cliente)).ToList()),
                LeerDatosPago = () => new DatosPagoAviso { Iban = IBAN, Titular = "Nueva Visión, S.A." },
                GenerarSaludos = nombres =>
                {
                    if (openAIFalla)
                    {
                        throw new Exception("OpenAI caído");
                    }
                    return Task.FromResult(nombres.ToDictionary(n => n, n => saludosOpenAI.TryGetValue(n, out string s) ? s : "Hola",
                        StringComparer.OrdinalIgnoreCase));
                },
                LeerFacturaPdf = (empresa, factura) =>
                {
                    pdfsPedidos.Add(factura);
                    return new byte[] { 1, 2, 3 };
                },
                Ahora = AHORA
            };

        private Task<ModoAvisoFacturasVencidas> Ejecutar(List<AvisoFacturaVencidaDTO> resultado)
            => AvisosFacturasVencidasJobsService.Procesar(Deps(resultado));

        private static AvisoClienteFacturasVencidasDTO AvisoCliente(params AvisoFacturaVencidaDTO[] facturas)
            => AvisosFacturasVencidasJobsService.AgruparPorCliente(facturas).Single();

        // ---------------------------------------------------------------- interruptor

        [TestMethod]
        public async Task Procesar_SinParametro_EstaApagadoYNoHaceNada()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, null);

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Apagado, modo);
            Assert.AreEqual(0, vecesCalculado, "Apagado ni siquiera consulta la BD");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_ValoresNoReconocidos_EstanApagados()
        {
            foreach (string valor in new[] { "0", "", "1", "activado", "sombras" })
            {
                Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, valor);
                Assert.AreEqual(ModoAvisoFacturasVencidas.Apagado, await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() }), valor);
            }
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_SiFallaLaLecturaDelParametro_QuedaApagado()
        {
            A.CallTo(() => lector.LeerParametro(A<string>._, A<string>._, A<string>._)).Throws(new Exception("BD caída"));

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Apagado, modo);
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>._)).MustNotHaveHappened();
        }

        [TestMethod]
        public async Task Procesar_DiasDelParametro_SinParametroCinco()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Sombra");
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS, "8");
            await Ejecutar(new List<AvisoFacturaVencidaDTO>());
            Assert.AreEqual(8, diasPedidos);

            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS, null);
            await Ejecutar(new List<AvisoFacturaVencidaDTO>());
            Assert.AreEqual(5, diasPedidos);

            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS_DIAS, "-3");
            await Ejecutar(new List<AvisoFacturaVencidaDTO>());
            Assert.AreEqual(5, diasPedidos);
        }

        [TestMethod]
        public void NormalizarIban_VariasCuentasEnUnaFrase()
        {
            Assert.AreEqual("ES06 2100 A o ES91 0049 B",
                AvisosFacturasVencidasJobsService.NormalizarIban("ES06 2100 A\r\nES91 0049 B\r\n"));
            Assert.IsNull(AvisosFacturasVencidasJobsService.NormalizarIban("  "));
        }

        // ---------------------------------------------------------------- sombra

        [TestMethod]
        public async Task Procesar_EnSombra_MandaUnSoloCorreoSoloAAdministracionYNoRegistraNada()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, " sombra ");

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO>
            {
                Aviso(),
                Aviso(factura: "NV2612001", nOrden: 2, destinatarios: "otro@cliente.es", cliente: "30676", motivo: "No se avisa. Retenido: envío INCIDENTADO")
            });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Sombra, modo);
            MailMessage mail = enviados.Single();
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, mail.To.Single().Address);
            Assert.AreEqual(0, mail.CC.Count);
            Assert.AreEqual(0, mail.Bcc.Count);
            StringAssert.Contains(mail.Subject, "[Sombra]");
            StringAssert.Contains(mail.Subject, "1 clientes (1 facturas) se avisarían");
            StringAssert.Contains(mail.Body, "NV2612000");
            StringAssert.Contains(mail.Body, "NV2612001");
            StringAssert.Contains(mail.Body, "INCIDENTADO");
            StringAssert.Contains(mail.Body, "cobros@ana.es");
            StringAssert.Contains(mail.Body, "Factura NV2612000 pendiente de pago", "Lleva el correo de muestra del primero");
            StringAssert.Contains(mail.Body, "1.er aviso", "Dice qué número de aviso sería");
            StringAssert.Contains(mail.Body, "04/10/2026", "Y cuándo tocaría el siguiente (10 días después del 1.º)");
            A.CallTo(() => almacen.Registrar(A<IEnumerable<AvisoFacturaVencidaRegistrado>>._)).MustNotHaveHappened();
            Assert.AreEqual(0, pdfsPedidos.Count, "La sombra no genera PDF");
        }

        [TestMethod]
        public async Task Procesar_EnSombraSinCandidatos_MandaIgualElCorreoParaSaberQueCorre()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Sombra");

            await Ejecutar(new List<AvisoFacturaVencidaDTO>());

            StringAssert.Contains(enviados.Single().Body, "Hoy no se avisaría a nadie");
        }

        [TestMethod]
        public async Task Procesar_EnSombra_LosQueNoTocanHoyVanComoEnEsperaConSuFecha()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Sombra");
            AvisoFacturaVencidaDTO enEspera = Aviso(tocaHoy: false, numeroAviso: 2);
            enEspera.FechaUltimoAviso = HOY.AddDays(-3);
            enEspera.FechaSiguienteAviso = HOY.AddDays(7);
            enEspera.NumeroUltimoAviso = 1;

            await Ejecutar(new List<AvisoFacturaVencidaDTO> { enEspera });

            MailMessage mail = enviados.Single();
            StringAssert.Contains(mail.Subject, "0 clientes (0 facturas) se avisarían, 1 en espera");
            StringAssert.Contains(mail.Body, "Avisables pero hoy no toca");
            StringAssert.Contains(mail.Body, "2.º aviso (aún no toca)");
            StringAssert.Contains(mail.Body, "01/10/2026");
            Assert.IsFalse(mail.Body.Contains("Muestra: el correo"), "Sin nadie a quien avisar no hay muestra");
        }

        [TestMethod]
        public async Task Procesar_EnSombra_IncluyeLosApuntesNegativosDeLosClientesQueSeQuedanFuera()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Sombra");
            negativos.Add(new ApunteNegativoClienteDTO { Cliente = "30676", Contacto = "0", Nombre = "ESTÉTICA LUZ", NOrden = 777, Fecha = new DateTime(2026, 9, 1), Importe = -80.25m, TipoApunte = "3", Concepto = "Transferencia a cuenta" });

            await Ejecutar(new List<AvisoFacturaVencidaDTO>
            {
                Aviso(cliente: "30676", nombre: "ESTÉTICA LUZ", motivo: SelectorAvisosFacturasVencidas.MOTIVO_CLIENTE_CON_NEGATIVOS)
            });

            string body = enviados.Single().Body;
            StringAssert.Contains(body, "Clientes con cobros o abonos pendientes de liquidar (1)");
            StringAssert.Contains(body, "777");
            StringAssert.Contains(body, "-80,25 €");
            StringAssert.Contains(body, "3 (pago)");
            StringAssert.Contains(body, "Transferencia a cuenta");
        }

        // ---------------------------------------------------------------- un aviso por cliente

        [TestMethod]
        public void AgruparPorCliente_TodasLasFacturasDelClienteEnUnAvisoYElNumeroEsElDelEfectoMasAntiguo()
        {
            List<AvisoClienteFacturasVencidasDTO> grupos = AvisosFacturasVencidasJobsService.AgruparPorCliente(new[]
            {
                Aviso(factura: "NV2612001", nOrden: 2, vencimiento: new DateTime(2026, 9, 15), numeroAviso: 1, importe: 100),
                Aviso(factura: "NV2612000", nOrden: 1, vencimiento: new DateTime(2026, 9, 1), numeroAviso: 3, importe: 50.5m),
                Aviso(factura: "NV2612002", nOrden: 3, cliente: "30676", importe: 10)
            });

            Assert.AreEqual(2, grupos.Count);
            AvisoClienteFacturasVencidasDTO ana = grupos.Single(g => g.Cliente == "15191");
            Assert.AreEqual(2, ana.Facturas.Count);
            Assert.AreEqual("NV2612000", ana.Facturas[0].Factura, "Ordenadas por vencimiento");
            Assert.AreEqual(3, ana.NumeroAviso, "El del efecto más antiguo");
            Assert.AreEqual(150.5m, ana.Total);
            CollectionAssert.AreEqual(new[] { "NV2612000", "NV2612001" }, ana.NumerosFactura);
        }

        [TestMethod]
        public void Plantilla_UnAvisoPorClienteConTablaTotalYBloqueParaCopiar()
        {
            AvisoClienteFacturasVencidasDTO aviso = AvisoCliente(
                Aviso(factura: "NV2615541", nOrden: 1, vencimiento: new DateTime(2026, 9, 1), importe: 1234.5m, cliente: "27120"),
                Aviso(factura: "NV2615600", nOrden: 2, vencimiento: new DateTime(2026, 9, 10), importe: 100, cliente: "27120"));
            aviso.Saludo = "Buenos días, Ana:";

            string texto = PlantillaAvisoFacturaVencida.CuerpoTexto(aviso, new DatosPagoAviso { Iban = IBAN, Titular = "Nueva Visión, S.A." });

            StringAssert.StartsWith(texto, "Buenos días, Ana:");
            StringAssert.Contains(texto, "figuran como pendientes de pago estas facturas");
            StringAssert.Contains(texto, "NV2615541");
            StringAssert.Contains(texto, "14/08/2026");
            StringAssert.Contains(texto, "01/09/2026");
            StringAssert.Contains(texto, "1.234,50 €");
            StringAssert.Contains(texto, "NV2615600");
            StringAssert.Contains(texto, "Total pendiente: 1.334,50 €");
            StringAssert.Contains(texto, "Si ya has hecho la transferencia, no hace falta que hagas nada");
            StringAssert.Contains(texto, "IBAN: " + IBAN);
            StringAssert.Contains(texto, "Titular: Nueva Visión, S.A.");
            StringAssert.Contains(texto, "Concepto: Cliente 27120 – NV2615541, NV2615600");
            StringAssert.Contains(texto, PlantillaAvisoFacturaVencida.FRASE_INMEDIATA);
            StringAssert.Contains(texto, "si hay algo en alguna factura que no te cuadra, responde a este correo y lo vemos");
            Assert.IsTrue(texto.EndsWith("Administración, Nueva Visión"));
            Assert.AreEqual("Facturas NV2615541, NV2615600 pendientes de pago", PlantillaAvisoFacturaVencida.Asunto(aviso));
        }

        [TestMethod]
        public void Plantilla_UnaSolaFactura_HablaEnSingularYSinTotal()
        {
            AvisoClienteFacturasVencidasDTO aviso = AvisoCliente(Aviso());
            aviso.Saludo = "Buenas tardes:";

            string texto = PlantillaAvisoFacturaVencida.CuerpoTexto(aviso, new DatosPagoAviso { Iban = IBAN });

            StringAssert.Contains(texto, "figura como pendiente de pago esta factura, ya vencida. Te la adjuntamos");
            Assert.IsFalse(texto.Contains("Total pendiente"));
            StringAssert.Contains(texto, "Concepto: Cliente 15191 – NV2612000");
            Assert.IsFalse(texto.Contains("Titular:"), "Sin titular no se inventa");
            Assert.AreEqual("Factura NV2612000 pendiente de pago", PlantillaAvisoFacturaVencida.Asunto(aviso));
        }

        [TestMethod]
        public void Plantilla_SegundoAvisoEnAdelante_EsRecordatorio()
        {
            AvisoClienteFacturasVencidasDTO aviso = AvisoCliente(Aviso(numeroAviso: 2));

            Assert.AreEqual("Recordatorio: Factura NV2612000 pendiente de pago", PlantillaAvisoFacturaVencida.Asunto(aviso));
            StringAssert.Contains(PlantillaAvisoFacturaVencida.CuerpoTexto(aviso, null), "Te volvemos a escribir porque");
        }

        [TestMethod]
        public void Plantilla_SinCuenta_NoDejaHuecosYSigueConElConcepto()
        {
            AvisoClienteFacturasVencidasDTO aviso = AvisoCliente(Aviso(nombre: " "));

            string texto = PlantillaAvisoFacturaVencida.CuerpoTexto(aviso, null);

            StringAssert.Contains(texto, "a la cuenta de Nueva Visión que figura en la factura, con el concepto de abajo");
            Assert.IsFalse(texto.Contains("IBAN:"));
            StringAssert.Contains(texto, "Concepto: Cliente 15191 – NV2612000");
        }

        [TestMethod]
        public void Plantilla_Html_EscapaLosDatosYLlevaTablaYBloque()
        {
            AvisoClienteFacturasVencidasDTO aviso = AvisoCliente(Aviso(nombre: "A & B <S.L.>"));
            aviso.Saludo = "Buenos días, A & B:";

            string html = PlantillaAvisoFacturaVencida.CuerpoHtml(aviso, new DatosPagoAviso { Iban = "ES06 <x>", Titular = "Nueva Visión, S.A." });

            StringAssert.Contains(html, "Buenos días, A &amp; B:");
            StringAssert.Contains(html, "ES06 &lt;x&gt;");
            Assert.IsFalse(html.Contains("<x>"));
            StringAssert.Contains(html, "<table");
            StringAssert.Contains(html, "<b>Concepto:</b> Cliente 15191 – NV2612000");
            StringAssert.Contains(html, "<b>IBAN:</b>");
            StringAssert.Contains(html, "<b>Titular:</b> Nueva Visión, S.A.");
        }

        // ---------------------------------------------------------------- saludo

        [TestMethod]
        public void Saludo_BuenosDiasHastaLas14YBuenasTardesDespues_ConNombreDePilaSiLoHay()
        {
            Assert.AreEqual("Buenos días, Susana:", PlantillaAvisoFacturaVencida.Saludo("Susana", new DateTime(2026, 9, 24, 7, 45, 0)));
            Assert.AreEqual("Buenos días:", PlantillaAvisoFacturaVencida.Saludo(null, new DateTime(2026, 9, 24, 13, 59, 0)));
            Assert.AreEqual("Buenas tardes, Susana:", PlantillaAvisoFacturaVencida.Saludo(" Susana ", new DateTime(2026, 9, 24, 14, 0, 0)));
            Assert.AreEqual("Buenas tardes:", PlantillaAvisoFacturaVencida.Saludo(" ", new DateTime(2026, 9, 24, 19, 0, 0)));
        }

        [TestMethod]
        public async Task ResolverSaludos_PrefiereLaPersonaDeContactoYSacaElNombreDePilaDeLoQueDevuelveElModelo()
        {
            saludosOpenAI["Susana"] = "¡Hola Susana!";
            saludosOpenAI["PELUQUERÍA ANA S.L."] = "Hola";
            saludosOpenAI["Rosa Martínez"] = "Hola Rosa";
            List<AvisoClienteFacturasVencidasDTO> avisos = AvisosFacturasVencidasJobsService.AgruparPorCliente(new[]
            {
                Aviso(cliente: "1", nombre: "PELUQUERÍA ANA S.L.", personaContacto: "Susana"),
                Aviso(cliente: "2", nombre: "PELUQUERÍA ANA S.L."),
                Aviso(cliente: "3", nombre: "Rosa Martínez"),
                Aviso(cliente: "4", nombre: null)
            });

            await AvisosFacturasVencidasJobsService.ResolverSaludos(avisos, Deps(new List<AvisoFacturaVencidaDTO>()));

            Assert.AreEqual("Buenos días, Susana:", avisos[0].Saludo, "Nombre de la persona de Cobros, no la razón social");
            Assert.AreEqual("Buenos días:", avisos[1].Saludo, "Empresa: genérico");
            Assert.AreEqual("Buenos días, Rosa:", avisos[2].Saludo, "Persona: nombre de pila");
            Assert.AreEqual("Buenos días:", avisos[3].Saludo, "Sin nombre: genérico");
        }

        [TestMethod]
        public async Task Procesar_SiOpenAIFalla_ElAvisoSaleConSaludoGenericoYNoSeBloquea()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");
            openAIFalla = true;

            await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso(personaContacto: "Susana") });

            MailMessage alCliente = enviados.Single(m => m.To.Any(t => t.Address == "cobros@ana.es"));
            StringAssert.Contains(alCliente.Body, "<p>Buenos días:</p>");
        }

        // ---------------------------------------------------------------- activo

        [TestMethod]
        public async Task Procesar_Activo_UnCorreoPorClienteDesdeAdministracionConPdfYRegistraCadaEfecto()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");
            saludosOpenAI["Susana"] = "Hola Susana";

            ModoAvisoFacturasVencidas modo = await Ejecutar(new List<AvisoFacturaVencidaDTO>
            {
                Aviso(factura: "NV2612000", nOrden: 1, personaContacto: "Susana", destinatarios: "cobros@ana.es, admin@ana.es"),
                Aviso(factura: "NV2612001", nOrden: 2, personaContacto: "Susana", destinatarios: "cobros@ana.es, admin@ana.es", importe: 10, numeroAviso: 2),
                Aviso(factura: "NV2612002", nOrden: 3, cliente: "30676", nombre: "ESTÉTICA LUZ", destinatarios: "facturas@luz.es"),
                Aviso(factura: "NV2612003", nOrden: 4, cliente: "40000", motivo: SelectorAvisosFacturasVencidas.MOTIVO_SIN_CORREO)
            });

            Assert.AreEqual(ModoAvisoFacturasVencidas.Activo, modo);
            List<MailMessage> aClientes = enviados.Where(m => !m.To.Any(t => t.Address == Constantes.Correos.CORREO_ADMON)).ToList();
            Assert.AreEqual(2, aClientes.Count, "Un correo por cliente, y al que no tiene correo no se le escribe");

            MailMessage ana = aClientes.Single(m => m.To.Any(t => t.Address == "cobros@ana.es"));
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, ana.From.Address);
            Assert.AreEqual(AvisosFacturasVencidasJobsService.NOMBRE_REMITENTE, ana.From.DisplayName);
            CollectionAssert.AreEquivalent(new[] { "cobros@ana.es", "admin@ana.es" }, ana.To.Select(t => t.Address).ToList());
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, ana.ReplyToList.Single().Address);
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, ana.Bcc.Single().Address);
            Assert.AreEqual("Facturas NV2612000, NV2612001 pendientes de pago", ana.Subject);
            Assert.IsTrue(ana.IsBodyHtml);
            StringAssert.Contains(ana.Body, "Buenos días, Susana:");
            StringAssert.Contains(ana.Body, "Cliente 15191 – NV2612000, NV2612001");
            StringAssert.Contains(ana.Body, IBAN);
            CollectionAssert.AreEquivalent(new[] { "NV2612000.pdf", "NV2612001.pdf" }, ana.Attachments.Select(a => a.Name).ToList());
            CollectionAssert.AreEquivalent(new[] { "NV2612000", "NV2612001", "NV2612002" }, pdfsPedidos);

            Assert.AreEqual(3, registrados.Count, "Un registro por efecto avisado");
            AvisoFacturaVencidaRegistrado r2 = registrados.Single(r => r.NumOrden == 2);
            Assert.AreEqual("1", r2.Empresa);
            Assert.AreEqual("15191", r2.Cliente);
            Assert.AreEqual("0", r2.Contacto);
            Assert.AreEqual("NV2612001", r2.Factura);
            Assert.AreEqual(2, r2.NumeroAviso, "Cada efecto lleva SU número");
            Assert.AreEqual(HOY, r2.Fecha);
            Assert.AreEqual(10m, r2.ImportePendiente);
            Assert.AreEqual("cobros@ana.es, admin@ana.es", r2.Destinatarios);
            Assert.IsFalse(registrados.Any(r => r.NumOrden == 4), "El que no se avisa no se registra");

            MailMessage resumen = enviados.Single(m => m.To.Any(t => t.Address == Constantes.Correos.CORREO_ADMON) && m.Bcc.Count == 0);
            StringAssert.Contains(resumen.Subject, "2 avisos mandados");
            StringAssert.Contains(resumen.Body, "Avisos mandados a clientes (2)");
        }

        [TestMethod]
        public async Task Procesar_Activo_ElEfectoQueNoTocaHoyVaEnElCorreoSiOtroDelClienteSiToca()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");

            await Ejecutar(new List<AvisoFacturaVencidaDTO>
            {
                Aviso(factura: "NV2612000", nOrden: 1, tocaHoy: false, numeroAviso: 2),
                Aviso(factura: "NV2612001", nOrden: 2, tocaHoy: true, numeroAviso: 1),
                Aviso(factura: "NV2612002", nOrden: 3, cliente: "30676", destinatarios: "facturas@luz.es", tocaHoy: false, numeroAviso: 2)
            });

            List<MailMessage> aClientes = enviados.Where(m => !m.To.Any(t => t.Address == Constantes.Correos.CORREO_ADMON)).ToList();
            MailMessage ana = aClientes.Single();
            StringAssert.Contains(ana.Body, "NV2612000");
            StringAssert.Contains(ana.Body, "NV2612001");
            CollectionAssert.AreEquivalent(new[] { 1, 2 }, registrados.Select(r => r.NumOrden).ToList());
            Assert.IsFalse(enviados.Any(m => m.To.Any(t => t.Address == "facturas@luz.es")), "Al que no le toca ninguna, hoy no se le escribe");
        }

        [TestMethod]
        public async Task Procesar_Activo_SiElCorreoNoSale_NoSeRegistraYElResumenLoDice()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");
            A.CallTo(() => correo.EnviarCorreoSMTP(A<MailMessage>.That.Matches(m => m.To.Any(t => t.Address == "cobros@ana.es"))))
                .Invokes((MailMessage m) => enviados.Add(Copiar(m)))
                .Returns(false);

            await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso() });

            Assert.AreEqual(0, registrados.Count);
            MailMessage resumen = enviados.Single(m => m.To.Any(t => t.Address == Constantes.Correos.CORREO_ADMON) && m.Bcc.Count == 0);
            StringAssert.Contains(resumen.Subject, "0 avisos mandados, 1 fallidos");
            StringAssert.Contains(resumen.Body, "El servidor de correo no ha aceptado el envío");
        }

        [TestMethod]
        public async Task Procesar_Activo_SiFallaElPdf_ElAvisoSaleSinElAdjunto()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");
            DependenciasAvisosFacturasVencidas deps = Deps(new List<AvisoFacturaVencidaDTO> { Aviso() });
            deps.LeerFacturaPdf = (e, f) => throw new Exception("Informe roto");

            await AvisosFacturasVencidasJobsService.Procesar(deps);

            MailMessage ana = enviados.Single(m => m.To.Any(t => t.Address == "cobros@ana.es"));
            Assert.AreEqual(0, ana.Attachments.Count);
            Assert.AreEqual(1, registrados.Count, "Se ha mandado: se registra");
        }

        [TestMethod]
        public async Task Procesar_Activo_ConNegativos_ElResumenLosLlevaParaLiquidar()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");
            negativos.Add(new ApunteNegativoClienteDTO { Cliente = "30676", Contacto = "0", Nombre = "ESTÉTICA LUZ", NOrden = 777, Fecha = new DateTime(2026, 9, 1), Importe = -80.25m, TipoApunte = "2", Concepto = "Abono" });

            await Ejecutar(new List<AvisoFacturaVencidaDTO>
            {
                Aviso(cliente: "30676", nombre: "ESTÉTICA LUZ", motivo: SelectorAvisosFacturasVencidas.MOTIVO_CLIENTE_CON_NEGATIVOS)
            });

            MailMessage resumen = enviados.Single();
            Assert.AreEqual(Constantes.Correos.CORREO_ADMON, resumen.To.Single().Address);
            StringAssert.Contains(resumen.Subject, "0 avisos mandados, 1 clientes con apuntes por liquidar");
            StringAssert.Contains(resumen.Body, "NO se avisa por tener cobros o abonos pendientes de liquidar (1)");
            StringAssert.Contains(resumen.Body, "777");
            StringAssert.Contains(resumen.Body, "-80,25 €");
            StringAssert.Contains(resumen.Body, "2 (cartera)");
        }

        [TestMethod]
        public async Task Procesar_Activo_SinNadaQueContar_NoMandaResumen()
        {
            Parametro(Constantes.ParametrosUsuario.AVISO_FACTURAS_VENCIDAS, "Activo");

            await Ejecutar(new List<AvisoFacturaVencidaDTO> { Aviso(motivo: SelectorAvisosFacturasVencidas.MOTIVO_SIN_CORREO) });

            Assert.AreEqual(0, enviados.Count);
        }
    }
}
