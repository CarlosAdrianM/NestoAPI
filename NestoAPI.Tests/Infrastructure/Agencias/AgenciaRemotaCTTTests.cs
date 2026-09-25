using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NestoAPI.Infraestructure.Agencias;
using NestoAPI.Infraestructure.Agencias.CTT;
using NestoAPI.Infraestructure.Agencias.Innovatrans;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json.Linq;

namespace NestoAPI.Tests.Infrastructure.Agencias
{
    /// <summary>
    /// NestoAPI#493: CTT Express por su API REST. Las respuestas de los fakes son las REALES que devolvió
    /// el sandbox (api-test.cttexpress.com) el 17/09/26, recortadas: si CTT cambia el contrato, estos
    /// tests siguen verdes pero la integración falla, así que ante una regresión en producción lo
    /// primero es comparar con una respuesta fresca.
    /// </summary>
    [TestClass]
    public class AgenciaRemotaCTTTests
    {
        private const string ALBARAN = "0082800082809800807576";

        private const string RESP_MANIFIESTO = @"{""shipping_data"":{""shipping_code"":""0082800082809800807576"",""client_references"":[""PRUEBA NESTOAPI 1"",""""],""pickup_code"":null,""return_shipping_code"":null,""items"":[{""item_order"":1,""item_code"":""0082800082809800807576001"",""item_synonym_code"":null,""item_external_code"":null}]}}";

        private const string ZPL_BULTO_1 = "^XA^CI28^PW799^LL1199^FO160,90^BY3^BCR,160,N,N,N,A^FD0082800082809800807576001^FS^FT532,1191^A0B,28,28^FH\\^FDESPAÑA^FS^PQ1,0,1,Y^XZ";
        private const string ZPL_BULTO_2 = "^XA^CI28^PW799^LL1199^FO160,90^BY3^BCR,160,N,N,N,A^FD0082800082809800807576002^FS^PQ1,0,1,Y^XZ";

        private static string RespEtiqueta(params string[] zpls)
            => new JObject
            {
                ["data"] = new JObject
                {
                    ["shipping_code"] = ALBARAN,
                    ["label_type_code"] = "ZPL",
                    ["model_type_code"] = "SINGLE",
                    ["label"] = null,
                    ["thermal_label"] = new JArray(zpls.Cast<object>().ToArray())
                }
            }.ToString();

        private const string RESP_YA_ANULADO = @"{""error"":{""error_code"":403,""error_datetime"":""2026-09-17T11:26:22.599691+02:00"",""error_type"":""FORBIDDEN_DUE_RESOURCE_STATUS"",""error_description"":""The operation cannot be processed considering the current status of the resource"",""error_extended_info"":{""message"":""Already cancelled shipping"",""details"":[""shipping_code""]},""translation_key"":""ALREADY_NULLED_SHIPPING"",""translation_values"":[],""help"":null}}";

        private static string RespSeguimiento(params (string code, string desc, string fecha, string incidencia)[] eventos)
        {
            var lista = new JArray();
            foreach (var e in eventos)
            {
                var detalle = new JObject { ["event_courier_code"] = "null", ["signee_name"] = "null" };
                if (e.incidencia != null)
                {
                    detalle["incident_type_name"] = e.incidencia;
                    detalle["incident_type_code"] = "2_INCT";
                }
                lista.Add(new JObject
                {
                    ["code"] = e.code,
                    ["description"] = e.desc,
                    ["type"] = "STATUS",
                    ["source"] = "ITEM_STATUS_V2",
                    ["event_date"] = e.fecha,
                    ["detail"] = detalle
                });
            }
            return new JObject
            {
                ["data"] = new JObject
                {
                    ["shipping_history"] = new JObject { ["item_code"] = ALBARAN + "001", ["events"] = lista },
                    ["shipping_code"] = ALBARAN,
                    ["shipping_type_code"] = "48P"
                },
                ["error"] = null
            }.ToString();
        }

        private static ConfiguracionCTT Config() => new ConfiguracionCTT(
            "https://api-test.cttexpress.com/integrations/", "id", "secreto", "8090300001",
            new RemitenteCTT { Nombre = "NUEVA VISION S.A.", Telefono = "916281914", CodigoPostal = "28110", Poblacion = "ALGETE", Direccion = "C/ RIO TIETAR 11" });

        private static DatosEnvioRemoto EnvioMadrid() => new DatosEnvioRemoto
        {
            Referencia = "926430",
            Nombre = "PRUEBA INTEGRACIÓN NESTO",
            Telefono = "916000000",
            Movil = "600000000",
            CodigoPostal = "28001",
            Poblacion = "MADRID",
            Direccion = "CALLE MAYOR 1",
            Email = "cliente@example.com",
            Peso = 3m,
            Bultos = 2,
            Reembolso = 0,
            Observaciones = "Llamar antes"
        };

        // ---- Manifiesto ----

        [TestMethod]
        public void ConstruirManifiesto_MapeaElEnvioAlContratoDeCTT()
        {
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), Config(), hoy: () => new DateTime(2026, 9, 17));

            JObject m = agencia.ConstruirManifiesto(EnvioMadrid());

            Assert.AreEqual("8090300001", (string)m["client_center_code"]);
            Assert.AreEqual("C48", (string)m["shipping_type_code"], "Madrid -> CTT 48h");
            Assert.AreEqual("926430", (string)m["client_references"][0], "La referencia (nº de pedido) viaja como referencia de cliente");
            Assert.AreEqual(3m, (decimal)m["shipping_weight_declared"]);
            Assert.AreEqual(2, (int)m["item_count"]);
            Assert.AreEqual(2, ((JArray)m["items"]).Count, "Un item por bulto");
            Assert.AreEqual(1.5m, (decimal)m["items"][0]["item_weight_declared"], "El peso se reparte entre los bultos");
            Assert.AreEqual(32m, (decimal)m["items"][0]["item_length_declared"], "Caja mediana por defecto");
            Assert.AreEqual("NUEVA VISION S.A.", (string)m["sender_name"]);
            Assert.AreEqual("28110", (string)m["sender_postal_code"]);
            Assert.AreEqual("ES", (string)m["recipient_country_code"]);
            Assert.AreEqual("28001", (string)m["recipient_postal_code"]);
            Assert.AreEqual("PRUEBA INTEGRACION NESTO", (string)m["recipient_name"], "Sin tildes: CTT las elimina en vez de transliterarlas");
            CollectionAssert.AreEqual(new[] { "600000000", "916000000" }, ((JArray)m["recipient_phones"]).Select(t => (string)t).ToArray(), "Móvil primero, sin vacíos");
            Assert.AreEqual("2026-09-17", (string)m["shipping_date"]);
            Assert.AreEqual("Llamar antes", (string)m["delivery"]["comments"]);
            Assert.AreEqual("cliente@example.com", (string)m["recipient_email_notify_address"], "Con email CTT avisa al cliente y le deja elegir punto de recogida");
            Assert.IsNull(m["additionals"], "Sin reembolso no hay additionals");
        }

        [TestMethod]
        public void Transliterar_QuitaTildesYEnyes_ComoQuiereCTT()
        {
            // Sandbox 17/09/26: CTT ELIMINA la ñ y las tildes de nuestros textos ("ESPAÑA Ñ" -> "ESPAA"),
            // así que se las quitamos nosotros bien: la letra base se conserva.
            Assert.AreEqual("PRUEBA ESPANA N MOSTOLES", AgenciaRemotaCTT.Transliterar("PRUEBA ESPAÑA Ñ MÓSTOLES"));
            Assert.AreEqual("Cocina de Ines, 3o Izq, portal 2a - 12 EUR", AgenciaRemotaCTT.Transliterar("Cocina de Inés, 3º Izq, portal 2ª - 12 €"));
            Assert.AreEqual("Francois Cancao", AgenciaRemotaCTT.Transliterar("François Cançao"));
            Assert.AreEqual(string.Empty, AgenciaRemotaCTT.Transliterar(null));
        }

        [TestMethod]
        public void ConstruirManifiesto_ConReembolso_AnadeElAdicionalREE()
        {
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), Config());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Reembolso = 50.52m;

            JObject m = agencia.ConstruirManifiesto(envio);

            JToken ree = m["additionals"][0];
            Assert.AreEqual("REE", (string)ree["additional_code"]);
            Assert.AreEqual(50.52m, (decimal)ree["additional_value"]);
            Assert.IsTrue((bool)ree["additional_flag"]);
        }

        [TestMethod]
        public void ConstruirManifiesto_Portugal_PaisPTYServicio48h()
        {
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), Config());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.CodigoPostal = "1000-001";

            JObject m = agencia.ConstruirManifiesto(envio);

            Assert.AreEqual("PT", (string)m["recipient_country_code"]);
            Assert.AreEqual("C48", (string)m["shipping_type_code"]);
        }

        [TestMethod]
        public void ConstruirManifiesto_ServicioUrgenteForzado_MandaEl24h()
        {
            // NestoAPI#505: el usuario ha elegido "CTT 24h" en la ventana de agencias.
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), Config());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Servicio = 24;

            JObject m = agencia.ConstruirManifiesto(envio);

            Assert.AreEqual("C24", (string)m["shipping_type_code"]);
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_ServicioQueCTTNoTiene_NoLlamaACTTYDevuelveElMotivo()
        {
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Servicio = 96; // BusinessParcel de GLS

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(envio);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "96");
            Assert.AreEqual(0, fake.Llamadas.Count, "No se registra nada en CTT");
        }

        [TestMethod]
        public void TipoServicio_PorCodigoPostal()
        {
            Assert.AreEqual("C48", MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal("28001"));   // Madrid
            Assert.AreEqual("C48", MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal("46001"));   // Peninsular
            Assert.AreEqual("C48", MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal("1000-001")); // Portugal
            Assert.AreEqual("CBA48", MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal("07001")); // Baleares
            Assert.AreEqual("CCA48", MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal("35001")); // Canarias
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void TipoServicio_CeutaMelilla_Lanza_PorqueNoHayTarifa()
        {
            MapeadorTipoServicioCTT.TipoServicioDesdeCodigoPostal("51001");
        }

        // ---- Insertar + etiqueta ----

        [TestMethod]
        public async Task InsertarYEtiquetar_Exito_DevuelveAlbaranBultosYZplPorBulto()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Manifiesto", 201, RESP_MANIFIESTO);
            fake.Responder("Etiqueta", 200, RespEtiqueta(ZPL_BULTO_1, ZPL_BULTO_2));
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsTrue(r.Exito, r.Error);
            Assert.AreEqual(ALBARAN, r.Albaran, "El albarán es el shipping_code de CTT");
            Assert.AreEqual(2, r.Bultos, "Los bultos salen del nº de etiquetas ZPL");
            Assert.IsTrue(r.Etiqueta.Exito);
            Assert.IsTrue(r.Etiqueta.EsZpl);
            Assert.AreEqual(string.Empty, r.Etiqueta.Codificacion, "ZPL en crudo, no base64");
            StringAssert.Contains(r.Etiqueta.Contenido, "807576001");
            StringAssert.Contains(r.Etiqueta.Contenido, "807576002");
            StringAssert.Contains(r.Etiqueta.Contenido, @"ESPA\c3\91A", "La eñe va en hex UTF-8 para que la Zebra la imprima");
            Assert.AreEqual("Manifiesto", fake.Llamadas[0].Operacion);
            Assert.AreEqual(HttpMethod.Post, fake.Llamadas[0].Metodo);
            Assert.AreEqual("manifest/v2.0/shippings", fake.Llamadas[0].Ruta);
            StringAssert.Contains(fake.Llamadas[1].Ruta, ALBARAN + "/shipping-labels?label_type_code=ZPL");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_SinPeso_NoLlamaACTT()
        {
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Peso = 0;

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(envio);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "peso");
            Assert.AreEqual(0, fake.Llamadas.Count);
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_CTTRechaza_DevuelveElMotivoSinLanzar()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Manifiesto", 400, @"{""error"":{""error_code"":400,""error_description"":""Wrong arguments"",""error_extended_info"":{""message"":""recipient_postal_code invalid""}}}");
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito);
            Assert.IsNull(r.Albaran);
            StringAssert.Contains(r.Error, "recipient_postal_code invalid");
        }

        [TestMethod]
        public async Task InsertarYEtiquetar_RegistradoPeroSinEtiqueta_ConservaElAlbaran()
        {
            // Como en Innovatrans: el envío YA existe en la agencia; el controlador guarda el albarán
            // aunque falle la etiqueta, para reimprimir después y no duplicar el envío.
            var fake = new FakeClienteRest();
            fake.Responder("Manifiesto", 201, RESP_MANIFIESTO);
            fake.Responder("Etiqueta", 404, @"{""error"":{""error_description"":""Not found""}}");
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(EnvioMadrid());

            Assert.IsFalse(r.Exito);
            Assert.AreEqual(ALBARAN, r.Albaran);
            StringAssert.Contains(r.Error, ALBARAN);
        }

        [TestMethod]
        public async Task Reimprimir_RangoDeBultos_DevuelveSoloEsasEtiquetas()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Etiqueta", 200, RespEtiqueta(ZPL_BULTO_1, ZPL_BULTO_2));
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            EtiquetaDataTrans e = await agencia.ReimprimirAsync(ALBARAN, 2, 2);

            Assert.IsTrue(e.Exito);
            StringAssert.Contains(e.Contenido, "807576002");
            Assert.IsFalse(e.Contenido.Contains("807576001"));
        }

        // ---- Anular / Modificar ----

        [TestMethod]
        public async Task Anular_204_Exito_Y_YaAnulado_TambienExito()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Anular", 204, string.Empty);
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());
            Assert.IsTrue((await agencia.AnularAsync(ALBARAN)).Exito);
            StringAssert.Contains(fake.Llamadas[0].Ruta, "rpc-cancel-shipping-by-shipping-code/" + ALBARAN);

            fake.Responder("Anular", 403, RESP_YA_ANULADO);
            ResultadoOperacionRemota r = await agencia.AnularAsync(ALBARAN);
            Assert.IsTrue(r.Exito, "Ya anulado en CTT = objetivo cumplido (idempotente)");
        }

        [TestMethod]
        public async Task Anular_OtroRechazo_DevuelveElMotivo()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Anular", 403, @"{""error"":{""error_code"":403,""translation_key"":""FORBIDDEN_DUE_RESOURCE_STATUS"",""error_extended_info"":{""message"":""Shipping already in transit""}}}");
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoOperacionRemota r = await agencia.AnularAsync(ALBARAN);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "Shipping already in transit");
        }

        [TestMethod]
        public async Task Modificar_AnulaYRegistraDeNuevo_ConAlbaranNuevo()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Anular", 204, string.Empty);
            fake.Responder("Manifiesto", 201, RESP_MANIFIESTO.Replace("807576", "814888"));
            fake.Responder("Etiqueta", 200, RespEtiqueta(ZPL_BULTO_1.Replace("807576", "814888")));
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.ModificarYEtiquetarAsync(EnvioMadrid(), ALBARAN);

            Assert.IsTrue(r.Exito, r.Error);
            Assert.AreEqual("0082800082809800814888", r.Albaran, "Albarán NUEVO: el controlador lo guarda en CodigoBarras");
            CollectionAssert.AreEqual(new[] { "Anular", "Manifiesto", "Etiqueta" }, fake.Llamadas.Select(l => l.Operacion).ToList());
        }

        [TestMethod]
        public async Task Modificar_SiNoDejaAnular_NoRegistraNada()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Anular", 410, @"{""error"":{""error_description"":""Gone, Shipment has expired and can no longer be modified""}}");
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.ModificarYEtiquetarAsync(EnvioMadrid(), ALBARAN);

            Assert.IsFalse(r.Exito);
            Assert.IsNull(r.Albaran);
            Assert.AreEqual(1, fake.Llamadas.Count, "Sin anulación no se registra un segundo envío");
        }

        // ---- NestoAPI#494: recogidas y retornos ----

        private static ConfiguracionCTT ConfigConRetornos()
        {
            ConfiguracionCTT c = Config();
            c.RetornosActivos = true;
            return c;
        }

        private static DatosEnvioRemoto RecogidaEnValencia()
        {
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Nombre = "PROVEEDOR DE PRUEBA";
            envio.Direccion = "CALLE COLÓN 5";
            envio.CodigoPostal = "46001";
            envio.Poblacion = "VALENCIA";
            envio.Retorno = AgenciaRemotaCTT.RETORNO_RECOGIDA_EN_ORIGEN;
            envio.Observaciones = "Preguntar por Ana";
            envio.Reembolso = -1m; // centinela "no cobrar"
            return envio;
        }

        [TestMethod]
        public void RecogidaEnOrigen_InvierteElEnvio_YPideLaRecogidaEnCasaDelCliente()
        {
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), ConfigConRetornos(), hoy: () => new DateTime(2026, 9, 25));
            DatosEnvioRemoto envio = RecogidaEnValencia();
            envio.FechaRecogida = new DateTime(2026, 9, 28);

            JObject m = agencia.ConstruirManifiesto(envio);

            Assert.AreEqual("PROVEEDOR DE PRUEBA", (string)m["sender_name"], "El remitente es el cliente/proveedor");
            Assert.AreEqual("46001", (string)m["sender_postal_code"]);
            Assert.AreEqual("CALLE COLON 5", (string)m["sender_address"]);
            CollectionAssert.AreEqual(new[] { "600000000", "916000000" }, ((JArray)m["sender_phones"]).Select(t => (string)t).ToArray());
            Assert.AreEqual("NUEVA VISION S.A.", (string)m["recipient_name"], "El destinatario es nuestro almacén");
            Assert.AreEqual("28110", (string)m["recipient_postal_code"]);
            Assert.AreEqual("C48", (string)m["shipping_type_code"], "Servicio por la zona del domicilio donde se recoge");
            Assert.IsTrue((bool)m["pickup"]["has_pickup_asap"], "CTT genera la orden de recogida en la dirección del remitente");
            Assert.AreEqual("2026-09-28", (string)m["pickup"]["date"]);
            Assert.AreEqual("09:00", (string)m["pickup"]["time"][0]["min_hourminute"]);
            Assert.AreEqual("18:00", (string)m["pickup"]["time"][0]["max_hourminute"]);
            Assert.AreEqual("Preguntar por Ana", (string)m["pickup"]["comments"]);
            Assert.IsNull(m["additionals"], "Sin reembolso ni RET");
            Assert.IsNull(m["recipient_email_notify_address"], "No se avisa al cliente como si fuera el destinatario");
        }

        [TestMethod]
        public void RecogidaEnOrigen_ConFechaPasada_PideHoy()
        {
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), ConfigConRetornos(), hoy: () => new DateTime(2026, 9, 25));
            DatosEnvioRemoto envio = RecogidaEnValencia();
            envio.FechaRecogida = new DateTime(2026, 9, 20);

            JObject m = agencia.ConstruirManifiesto(envio);

            Assert.AreEqual("2026-09-25", (string)m["pickup"]["date"]);
        }

        [TestMethod]
        public void ConRetorno_EsUnEnvioNormalConElAdicionalRET_YElReembolsoSeMantiene()
        {
            var agencia = new AgenciaRemotaCTT(new FakeClienteRest(), ConfigConRetornos());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Retorno = AgenciaRemotaCTT.RETORNO_CON_RETORNO;
            envio.Reembolso = 20m;

            JObject m = agencia.ConstruirManifiesto(envio);

            Assert.AreEqual("28001", (string)m["recipient_postal_code"], "Se entrega al cliente como siempre");
            Assert.IsNull(m["pickup"]);
            CollectionAssert.AreEquivalent(new[] { "REE", "RET" }, ((JArray)m["additionals"]).Select(a => (string)a["additional_code"]).ToArray());
        }

        [TestMethod]
        public async Task Retornos_ConElInterruptorApagado_SeRechazanSinLlamarACTT()
        {
            // Nunca sale como envío normal algo pedido como recogida: el paquete iría al cliente.
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(RecogidaEnValencia());

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, AgenciaRemotaCTT.CLAVE_RETORNOS_ACTIVOS);
            Assert.AreEqual(0, fake.Llamadas.Count);
        }

        [TestMethod]
        public async Task RecogidaEnOrigen_ConReembolso_SeRechaza()
        {
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, ConfigConRetornos());
            DatosEnvioRemoto envio = RecogidaEnValencia();
            envio.Reembolso = 30m;

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(envio);

            Assert.IsFalse(r.Exito);
            StringAssert.Contains(r.Error, "reembolso");
            Assert.AreEqual(0, fake.Llamadas.Count);
        }

        [TestMethod]
        public async Task TipoDeRetornoQueCTTNoTiene_SeRechaza()
        {
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, ConfigConRetornos());
            DatosEnvioRemoto envio = EnvioMadrid();
            envio.Retorno = 7;

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(envio);

            Assert.IsFalse(r.Exito);
            Assert.AreEqual(0, fake.Llamadas.Count);
        }

        [TestMethod]
        public async Task Modificar_SiElEnvioNuevoNoSePuedeRegistrar_NoAnulaElAnterior()
        {
            // Retornos apagados: antes se anulaba el albarán y luego fallaba el registro -> sin ninguno.
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            ResultadoTramitacionRemota r = await agencia.ModificarYEtiquetarAsync(RecogidaEnValencia(), ALBARAN);

            Assert.IsFalse(r.Exito);
            Assert.AreEqual(0, fake.Llamadas.Count, "No se anula nada");
        }

        [TestMethod]
        public async Task RecogidaEnOrigen_ConInterruptorEncendido_RegistraYDevuelveElAlbaran()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Manifiesto", 201, RESP_MANIFIESTO);
            fake.Responder("Etiqueta", 200, RespEtiqueta(ZPL_BULTO_1));
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, ConfigConRetornos());

            ResultadoTramitacionRemota r = await agencia.InsertarYEtiquetarAsync(RecogidaEnValencia());

            Assert.IsTrue(r.Exito, r.Error);
            Assert.AreEqual(ALBARAN, r.Albaran, "El seguimiento de la recogida va por el mismo shipping_code");
            var cuerpo = (JObject)fake.Llamadas.First(l => l.Operacion == "Manifiesto").Cuerpo;
            Assert.IsTrue((bool)cuerpo["pickup"]["has_pickup_asap"]);
        }

        [TestMethod]
        public void InterruptorRetornos_SoloSeEnciendeConUnValorExplicito()
        {
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.RetornosActivos("1"));
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.RetornosActivos(" true "));
            Assert.IsTrue(NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.RetornosActivos("Sí"));
            Assert.IsFalse(NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.RetornosActivos(null));
            Assert.IsFalse(NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.RetornosActivos(""));
            Assert.IsFalse(NestoAPI.Infraestructure.Agencias.Perfiles.PerfilAgenciaCTT.RetornosActivos("0"));
        }

        // ---- Seguimiento ----

        [TestMethod]
        public async Task Seguimiento_MapeaLosEstadosDeCTT()
        {
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            fake.Responder("Seguimiento", 200, RespSeguimiento(("0000", "MANIFESTADO O GRABADO", "2026-09-17T09:25:52.546+00:00", null)));
            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, (await agencia.ConsultarSeguimientoAsync(ALBARAN)).Estado);

            fake.Responder("Seguimiento", 200, RespSeguimiento(
                ("0000", "MANIFESTADO O GRABADO", "2026-09-17T09:25:52.546+00:00", null),
                ("0900", "In Transit", "2026-09-17T17:07:32.660+00:00", null),
                ("1500", "Out for Delivery", "2026-09-18T07:49:31.565+00:00", null)));
            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, (await agencia.ConsultarSeguimientoAsync(ALBARAN)).Estado,
                "En tránsito/en reparto sigue TRAMITADO: EnCurso (0) es la etiqueta sin tramitar (23/09/26)");

            fake.Responder("Seguimiento", 200, RespSeguimiento(
                ("0000", "MANIFESTADO O GRABADO", "2026-09-22T09:25:52.546+00:00", null),
                ("0500", "ENVÍO RECOGIDO", "2026-09-22T17:07:32.660+00:00", null)));
            SeguimientoEnvioRemoto recogido = await agencia.ConsultarSeguimientoAsync(ALBARAN);
            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, recogido.Estado, "Los 20 envíos del 22/09 volvieron a En curso por esto");
            Assert.AreEqual("ENVÍO RECOGIDO", recogido.Detalle);
            CollectionAssert.Contains(AgenciaRemotaCTT.CODIGOS_CONOCIDOS, "0500", "Si no, cada pasada del poll deja un aviso en ELMAH por envío");

            fake.Responder("Seguimiento", 200, RespSeguimiento(
                ("1500", "Out for Delivery", "2026-09-18T07:49:31.565+00:00", null),
                ("1600", "Failed Delivery", "2026-09-18T10:12:55.470+00:00", "Recipient Not Responding")));
            SeguimientoEnvioRemoto incidencia = await agencia.ConsultarSeguimientoAsync(ALBARAN);
            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, incidencia.Estado);
            StringAssert.Contains(incidencia.Detalle, "Recipient Not Responding");

            fake.Responder("Seguimiento", 200, RespSeguimiento(
                ("1600", "Failed Delivery", "2026-09-18T10:12:55.470+00:00", "Recipient Not Responding"),
                ("2100", "Delivered", "2026-09-19T10:20:34.173+00:00", null)));
            SeguimientoEnvioRemoto entregado = await agencia.ConsultarSeguimientoAsync(ALBARAN);
            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, entregado.Estado);
            Assert.IsNotNull(entregado.FechaEntrega);
            Assert.AreEqual(new DateTime(2026, 9, 19), entregado.FechaEntrega.Value.Date);

            fake.Responder("Seguimiento", 200, RespSeguimiento(
                ("0000", "MANIFESTADO O GRABADO", "2026-09-17T09:25:52.546+00:00", null),
                ("3000", "ENVÍO ANULADO", "2026-09-17T09:26:22.290+00:00", null)));
            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, (await agencia.ConsultarSeguimientoAsync(ALBARAN)).Estado,
                "Anulado no es un estado nuestro: no se persiste (NestoAPI#264)");
        }

        [TestMethod]
        public async Task Seguimiento_CodigoDesconocido_SeInterpretaPorLaDescripcion()
        {
            var fake = new FakeClienteRest();
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            fake.Responder("Seguimiento", 200, RespSeguimiento(("2500", "DEVUELTO A ORIGEN", "2026-09-20T10:00:00.000+00:00", null)));
            Assert.AreEqual(EstadoEnvioSeguimiento.Devuelto, (await agencia.ConsultarSeguimientoAsync(ALBARAN)).Estado);

            fake.Responder("Seguimiento", 200, RespSeguimiento(("1300", "EN REPARTO ZONA", "2026-09-20T10:00:00.000+00:00", null)));
            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, (await agencia.ConsultarSeguimientoAsync(ALBARAN)).Estado);
        }

        [TestMethod]
        public async Task Seguimiento_404_EsDesconocido_NoUnEstado()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Seguimiento", 404, @"{""error"":{""error_description"":""Not found""}}");
            IAgenciaRemota agencia = new AgenciaRemotaCTT(fake, Config());

            Assert.AreEqual(EstadoEnvioSeguimiento.Desconocido, (await agencia.ConsultarSeguimientoAsync(ALBARAN)).Estado);
        }

        // ---- Seguimiento por lotes (listado por fechas) y cupo (23/09/26) ----

        private static string RespPorFechas(int actual, int? siguiente, int ultima, params (string codigo, string estado, string fecha, string incidencia)[] envios)
        {
            var data = new JArray();
            foreach (var e in envios)
            {
                data.Add(new JObject
                {
                    ["shipping_code"] = e.codigo,
                    ["shipping_date"] = "2026-09-22",
                    ["shipping_status_code"] = e.estado,
                    ["shipping_status_datetime"] = e.fecha,
                    ["incident_type_code"] = e.incidencia == null ? null : "INC001",
                    ["incident_type_desc"] = e.incidencia,
                    ["client_center_code"] = "8090300001"
                });
            }
            return new JObject
            {
                ["data"] = data,
                ["pagination"] = new JObject
                {
                    ["page_limit"] = 50,
                    ["record_count"] = envios.Length,
                    ["page_offsets"] = new JObject { ["current"] = actual, ["next"] = siguiente, ["last"] = ultima }
                }
            }.ToString();
        }

        [TestMethod]
        public async Task SeguimientoPorFechas_DosPaginas_DevuelveTodosLosEnviosTraducidos()
        {
            var fake = new FakeClienteRest();
            fake.ResponderSecuencia("SeguimientoPorFechas",
                (200, RespPorFechas(1, 2, 2,
                    ("0082800082809772517327", "0500", "2026-09-22T16:00:00Z", null),
                    ("0082800082809772528136", "1600", "2026-09-23T09:10:00Z", "Ausente"))),
                (200, RespPorFechas(2, null, 2,
                    ("0082800082809772570062", "2100", "2026-09-23T11:30:00Z", null))));
            var agencia = new AgenciaRemotaCTT(fake, Config());

            IReadOnlyDictionary<string, SeguimientoEnvioRemoto> estados = await agencia.ConsultarSeguimientosAsync(new DateTime(2026, 9, 15), new DateTime(2026, 9, 23));

            Assert.AreEqual(2, fake.Llamadas.Count, "Una llamada por página, no una por envío");
            StringAssert.Contains(fake.Llamadas[0].Ruta, "page_offsets=1");
            StringAssert.Contains(fake.Llamadas[1].Ruta, "page_offsets=2");
            StringAssert.Contains(fake.Llamadas[0].Ruta, "client_center_code=8090300001");
            StringAssert.Contains(fake.Llamadas[0].Ruta, "shipping_date=2026-09-15[range]2026-09-23");
            StringAssert.Contains(fake.Llamadas[0].Ruta, "mapping_table_code=APITRACK");

            Assert.AreEqual(3, estados.Count);
            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, estados["0082800082809772517327"].Estado);
            Assert.AreEqual("ENVÍO RECOGIDO", estados["0082800082809772517327"].Detalle);
            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, estados["0082800082809772528136"].Estado);
            StringAssert.Contains(estados["0082800082809772528136"].Detalle, "Ausente");
            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, estados["0082800082809772570062"].Estado);
            Assert.AreEqual(new DateTime(2026, 9, 23), estados["0082800082809772570062"].FechaEntrega.Value.Date);
        }

        [TestMethod]
        public void Traducir_2400SinDescripcion_EsNuevoRepartoYSigueTramitado()
        {
            // 24/09/26: envío 249000 (cliente ausente, sale de nuevo). Llegaba sin descripción y dejaba
            // un aviso en ELMAH en cada pasada del poll.
            var resultado = AgenciaRemotaCTT.Traducir("2400", "", null, null, "0082800082809772536836");

            Assert.AreEqual(EstadoEnvioSeguimiento.Tramitado, resultado.Estado);
            Assert.AreEqual("NUEVO REPARTO", resultado.Detalle);
            CollectionAssert.Contains(AgenciaRemotaCTT.CODIGOS_CONOCIDOS, "2400");
        }

        [TestMethod]
        public void Traducir_FinalesDeLaTablaOficial()
        {
            Assert.AreEqual(EstadoEnvioSeguimiento.Entregado, AgenciaRemotaCTT.Traducir("2110", "", null, null, "A").Estado);
            Assert.AreEqual(EstadoEnvioSeguimiento.Devuelto, AgenciaRemotaCTT.Traducir("2500", "", null, null, "A").Estado);
            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, AgenciaRemotaCTT.Traducir("2600", "", null, null, "A").Estado);
            Assert.AreEqual(EstadoEnvioSeguimiento.Incidentado, AgenciaRemotaCTT.Traducir("1600", "REPARTO FALLIDO", "Ausente", null, "A").Estado);
        }

        [TestMethod]
        public void Traducir_NingunCodigoConocidoEsEnCurso()
        {
            // 22/09/26: EnCurso = "sin tramitar" en EnviosAgencia; el poll dejaba de seguirlos.
            foreach (string codigo in AgenciaRemotaCTT.CODIGOS_CONOCIDOS)
            {
                Assert.AreNotEqual(EstadoEnvioSeguimiento.EnCurso, AgenciaRemotaCTT.Traducir(codigo, "", null, null, "A").Estado, codigo);
            }
        }

        [TestMethod]
        public void SeguimientoPorFechas_CadaCodigoSignificaLoMismoQueEnElIndividual()
        {
            // Una sola tabla de traducción: el poll (por fechas) y el «Actualizar estado» (individual)
            // no pueden dar estados distintos para el mismo código de CTT.
            foreach (string codigo in AgenciaRemotaCTT.CODIGOS_CONOCIDOS)
            {
                var individual = AgenciaRemotaCTT.InterpretarEventos((JArray)JObject.Parse(RespSeguimiento(
                    (codigo, "DESCRIPCION", "2026-09-23T10:00:00Z", null)))["data"]["shipping_history"]["events"]);
                var porFechas = AgenciaRemotaCTT.InterpretarRegistroPorFechas(JObject.Parse(
                    RespPorFechas(1, null, 1, ("0082800082809772517327", codigo, "2026-09-23T10:00:00Z", null)))["data"][0]);

                Assert.AreEqual(individual.Estado, porFechas.Estado, "Código " + codigo);
            }
        }

        [TestMethod]
        public async Task SeguimientoIndividual_Cupo429_LanzaCupoAgotadoSinReintentarComoTransitorio()
        {
            var fake = new FakeClienteRest();
            fake.Responder("Seguimiento", 429, @"{""statusCode"": 429, ""message"": ""Quota has been exceeded""}");
            var agencia = new AgenciaRemotaCTT(fake, Config());

            CupoAgenciaAgotadoException ex = await Assert.ThrowsExceptionAsync<CupoAgenciaAgotadoException>(
                () => agencia.ConsultarSeguimientoAsync(ALBARAN));

            Assert.IsFalse(ex.EsTransitoria, "Reintentar a los pocos segundos solo gasta más cupo");
        }

        [TestMethod]
        public async Task SeguimientoPorFechas_Cupo429_LanzaCupoAgotado()
        {
            var fake = new FakeClienteRest();
            fake.Responder("SeguimientoPorFechas", 429, @"{""statusCode"": 429, ""message"": ""Quota has been exceeded""}");
            var agencia = new AgenciaRemotaCTT(fake, Config());

            _ = await Assert.ThrowsExceptionAsync<CupoAgenciaAgotadoException>(
                () => agencia.ConsultarSeguimientosAsync(new DateTime(2026, 9, 15), new DateTime(2026, 9, 23)));
        }

        [TestMethod]
        public void LeerRetryAfter_ConSegundos_DevuelveLaEspera()
        {
            var http = new HttpResponseMessage((System.Net.HttpStatusCode)429);
            http.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(120));

            Assert.AreEqual(TimeSpan.FromSeconds(120), ClienteRestCTT.LeerRetryAfter(http));
            Assert.IsNull(ClienteRestCTT.LeerRetryAfter(new HttpResponseMessage((System.Net.HttpStatusCode)429)));
        }

        // ---- ZPL y URL pública ----

        [TestMethod]
        public void NormalizadorZpl_CodificaLaEnyeEnHexUtf8_YEsIdempotente()
        {
            string zpl = @"^FT532,1191^A0B,28,28^FH\^FDESPAÑA^FS^FT1,1^FH\^FDMÁLAGA^FS^FDsin FH: ñ^FS";
            string una = NormalizadorZplCTT.CodificarNoAscii(zpl);
            Assert.AreEqual(@"^FT532,1191^A0B,28,28^FH\^FDESPA\c3\91A^FS^FT1,1^FH\^FDM\c3\81LAGA^FS^FDsin FH: ñ^FS", una,
                "Solo los campos con ^FH\\ se codifican");
            Assert.AreEqual(una, NormalizadorZplCTT.CodificarNoAscii(una));
        }

        [TestMethod]
        public void SeguimientoPublico_CTT_LocalizadorConElAlbaran()
        {
            var datos = new DatosSeguimientoEnvio { AgenciaNombre = "CTT", CodigoSeguimiento = ALBARAN };
            Assert.IsTrue(RegistroSeguimientoAgencias.AgenciaConocida("CTT"));
            Assert.AreEqual("https://www.cttexpress.com/localizador-de-envios?sc=" + ALBARAN, RegistroSeguimientoAgencias.ConstruirUrl(datos));
        }

        // ---- Fake del cliente REST: responde por operación y guarda lo que se le pidió ----

        private class Llamada
        {
            public string Operacion;
            public HttpMethod Metodo;
            public string Ruta;
            public object Cuerpo;
        }

        private class FakeClienteRest : IClienteRestCTT
        {
            private readonly Dictionary<string, (int codigo, string cuerpo)> _respuestas = new Dictionary<string, (int, string)>();
            private readonly Dictionary<string, Queue<(int codigo, string cuerpo)>> _secuencias = new Dictionary<string, Queue<(int, string)>>();
            public readonly List<Llamada> Llamadas = new List<Llamada>();

            public void Responder(string operacion, int codigo, string cuerpo) => _respuestas[operacion] = (codigo, cuerpo);

            /// <summary>Respuestas distintas en llamadas sucesivas a la misma operación (paginación).</summary>
            public void ResponderSecuencia(string operacion, params (int codigo, string cuerpo)[] respuestas)
                => _secuencias[operacion] = new Queue<(int, string)>(respuestas);

            public Task<RespuestaCTT> EnviarAsync(HttpMethod metodo, string rutaRelativa, object cuerpo, string operacion)
            {
                Llamadas.Add(new Llamada { Operacion = operacion, Metodo = metodo, Ruta = rutaRelativa, Cuerpo = cuerpo });
                if (_secuencias.TryGetValue(operacion, out Queue<(int codigo, string cuerpo)> cola) && cola.Count > 0)
                {
                    (int codigo, string cuerpo) siguiente = cola.Dequeue();
                    return Task.FromResult(new RespuestaCTT { Codigo = siguiente.codigo, Cuerpo = siguiente.cuerpo });
                }
                if (!_respuestas.TryGetValue(operacion, out (int codigo, string cuerpo) r))
                {
                    throw new InvalidOperationException("El test no preparó respuesta para " + operacion);
                }
                return Task.FromResult(new RespuestaCTT { Codigo = r.codigo, Cuerpo = r.cuerpo });
            }
        }
    }
}
