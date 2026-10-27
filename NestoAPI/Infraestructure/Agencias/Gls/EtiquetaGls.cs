using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>NestoAPI#552 (fase 4): el transporte de GetTSP (código de ruta de GLS que va en la etiqueta). Se fakea en los tests.</summary>
    public interface IClienteTspGls : IClienteSoapGls
    {
    }

    /// <summary>
    /// NestoAPI#552 (fase 4): GetTSP del WS b2b de GLS, como AgenciaASM.GetTSPAsync de Nesto (mismo host wsclientes, mismo
    /// SOAPAction y 15 s de espera como mucho). Es una CONSULTA: no graba nada en GLS. Nesto la hace en cada impresión.
    /// </summary>
    public class ClienteTspGls : ClienteSoapGls, IClienteTspGls
    {
        public const string OPERACION_GET_TSP = "GetTSP";
        public const string URL_GET_TSP = "https://wsclientes.asmred.com/b2b.asmx?op=" + OPERACION_GET_TSP;

        private static readonly HttpClient Cliente15s = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        public ClienteTspGls(HttpClient http = null)
            : base(OPERACION_GET_TSP, URL_GET_TSP, http ?? Cliente15s)
        {
        }

        /// <summary>El sobre de AgenciaASM.GetTSPAsync (origen fijo: España, 28119 Algete).</summary>
        public static string ConstruirSoap(EnviosAgencia envio)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body>" +
                "<" + OPERACION_GET_TSP + " xmlns=\"http://www.asmred.com/\">" +
                "<Request>" +
                "<CodPaisOrg>" + EtiquetaGls.PAIS_ORIGEN + "</CodPaisOrg>" +
                "<CPOrg>" + EtiquetaGls.CP_ORIGEN + "</CPOrg>" +
                "<CodPaisDst>" + envio.Pais.ToString(CultureInfo.InvariantCulture) + "</CodPaisDst>" +
                "<CPDst>" + SecurityElement.Escape(envio.CodPostal?.Trim() ?? string.Empty) + "</CPDst>" +
                "<CodServicio>" + envio.Servicio.ToString(CultureInfo.InvariantCulture) + "</CodServicio>" +
                "<CodHorario>" + envio.Horario.ToString(CultureInfo.InvariantCulture) + "</CodHorario>" +
                "</Request>" +
                "</" + OPERACION_GET_TSP + ">" +
                "</soap:Body>" +
                "</soap:Envelope>";
        }
    }

    /// <summary>NestoAPI#552 (fase 4): lectura de la respuesta de GetTSP, como AgenciaASM.ParseCodTSP.</summary>
    public static class RespuestaTspGls
    {
        /// <summary>El CodTSP; «N/A» si la respuesta no lo trae y «ERROR» si no se entiende (lo mismo que imprime Nesto).</summary>
        public static string Interpretar(string respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                // AgenciaASM.GetTSPAsync: respuesta vacía = «Error».
                return "Error";
            }
            try
            {
                XDocument documento = XDocument.Parse(respuesta.TrimStart('﻿', ' ', '\r', '\n', '\t'));
                string codigo = documento.Descendants(PeticionGls.Ns + "CodTSP").FirstOrDefault()?.Value;
                return string.IsNullOrWhiteSpace(codigo) ? "N/A" : codigo;
            }
            catch (XmlException)
            {
                return "ERROR";
            }
        }

        /// <summary>
        /// NestoAPI#552: ¿es un código de ruta de verdad? «N/A», «Error» y «ERROR» son lo que <see cref="Interpretar"/> da cuando
        /// GLS no lo trae o no se entiende: con ellos no se imprime (TSP obligatorio, decisión de Carlos del 27/10/26).
        /// </summary>
        public static bool EsCodigoValido(string tsp)
            => !string.IsNullOrWhiteSpace(tsp)
            && !string.Equals(tsp.Trim(), "N/A", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(tsp.Trim(), "ERROR", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// NestoAPI#552 (fase 4): la etiqueta ZPL de un envío de GLS montada en el servidor, la misma que AgenciaASM.imprimirEtiqueta
    /// de Nesto (una por bulto, código 2 de 5 con el código de barras + el bulto en 3 cifras). NO graba el envío en GLS: se
    /// sigue trabajando como siempre, primero imprimir y tramitar al cerrar el día («Tramitar todos»). Decisión de Carlos del
    /// 21/10/26 para poder imprimir las etiquetas de GLS desde Ariadna.
    /// </summary>
    public static class EtiquetaGls
    {
        public const string PAIS_ORIGEN = "34";
        public const string CP_ORIGEN = "28119";

        private const string NOMBRE_REMITENTE = "NUEVA VISIÓN, S.A.";
        private const string DIRECCION_REMITENTE = "c/ Río Tiétar, 11";
        private const string POBLACION_REMITENTE = "Algete";
        private const string PROVINCIA_REMITENTE = "Madrid";
        private const string CODIGO_REMITENTE = "791/1664";

        /// <summary>El nombre del servicio que sale en la etiqueta (las tarifas de AgenciaASM.ListaServicios).</summary>
        public static string NombreServicio(short servicio)
        {
            switch (servicio)
            {
                case 6: return "Insular marítimo";
                case PeticionGls.SERVICIO_BUSINESSPARCEL: return "BusinessParcel";
                default: return "Servicio " + servicio.ToString(CultureInfo.InvariantCulture);  // Nesto#437
            }
        }

        /// <summary>
        /// La ZPL de todos los bultos, igual que AgenciaASM.imprimirEtiqueta. La plaza (nemónico y nombre) es la del envío.
        /// </summary>
        /// <exception cref="InvalidOperationException">Si el envío no tiene código de barras (Nesto lanza lo mismo).</exception>
        public static string ConstruirZpl(EnviosAgencia envio, string codigoTsp)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            string codigoBarras = envio.CodigoBarras?.Trim();
            if (string.IsNullOrEmpty(codigoBarras))
            {
                throw new InvalidOperationException("El envío debe tener un código de barras asignado para poder imprimir la etiqueta.");
            }

            string inicioCodBarras = codigoBarras.Length <= 14 ? codigoBarras : codigoBarras.Substring(0, 14).Trim();
            string cliente = envio.Cliente?.Trim() ?? string.Empty;
            string pedido = envio.Pedido?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
            string bultos = envio.Bultos.ToString(CultureInfo.InvariantCulture);
            var zpl = new StringBuilder();

            for (int i = 1; i <= envio.Bultos; i++)
            {
                Linea(zpl, "^XA");
                Linea(zpl, "^CI27");
                Linea(zpl, "^BY3");
                Linea(zpl, "^LL900");
                Linea(zpl, "^FO10,50^AUN,50,50^FDGLS^FS");
                Linea(zpl, "^FO200,50^AUN,50,50^FD" + inicioCodBarras + "^FS");
                Linea(zpl, "^FO200,130^GB775,0,2,B,3^FS");
                Linea(zpl, "^FO170,125^AQB,1,1^FDDESTINATARIO^FS");
                Linea(zpl, "^FO200,130^ARN,1,1^FD" + Texto(envio.Nombre) + "^FS");
                Linea(zpl, "^FO200,160^ARN,1,1^FD" + Texto(envio.Direccion) + "^FS");
                Linea(zpl, "^FO200,190^ARN,1,1^FD" + Texto(envio.CodPostal) + ". " + Texto(envio.Poblacion) + "^FS");
                Linea(zpl, "^FO200,220^ARN,1,1^FD" + Texto(envio.Provincia) + "^FS");
                Linea(zpl, "^FO600,220^ARN,1,1^FD" + Texto(envio.Telefono) + "^FS");
                Linea(zpl, "^FO10,160^ARN,1,1^FD" + envio.Fecha.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture) + "^FS");
                Linea(zpl, "^FO90,230^AUN,50,50^FD" + Texto(codigoTsp) + "^FS");
                Linea(zpl, "^FO300,260^GB775,0,2,B,3^FS");
                Linea(zpl, "^FO270,265^AQB,1,1^FDREMITENTE^FS");
                Linea(zpl, "^FO300,270^APN,1,1^FD" + NOMBRE_REMITENTE + "^FS");
                Linea(zpl, "^FO300,290^APN,1,1^FD" + DIRECCION_REMITENTE + "^FS");
                Linea(zpl, "^FO300,310^APN,1,1^FD" + CP_ORIGEN + ". " + POBLACION_REMITENTE + "^FS");
                Linea(zpl, "^FO300,330^APN,1,1^FD" + PROVINCIA_REMITENTE + "^FS");
                Linea(zpl, "^FO600,350^APN,1,1^FD(" + CODIGO_REMITENTE + ")^FS");
                Linea(zpl, "^FO300,380^GB775,0,2,B,3^FS");
                Linea(zpl, "^FO270,390^AQB,1,1^FDOBSERV^FS");
                Linea(zpl, "^FO300,390^APN,1,1^FD" + Texto(envio.Observaciones) + "^FS");
                Linea(zpl, "^FO300,410^APN,1,1^FD-----^FS");
                Linea(zpl, "^FO300,430^APN,1,1^FD-----^FS");
                Linea(zpl, "^FO300,450^APN,1,1^FD-----^FS");
                Linea(zpl, "^FO300,470^GB775,0,2,B,3^FS");
                Linea(zpl, "^FO300,475^ATN,10,10^FDCOURIER^FS");
                Linea(zpl, "^FO300,510^ATN,10,10^FD" + NombreServicio(envio.Servicio) + " (" + envio.Horario.ToString(CultureInfo.InvariantCulture) + ")^FS");
                Linea(zpl, "^FO300,530^AVN,200,200^FD" + Texto(envio.Nemonico) + "^FS");
                Linea(zpl, "^FO300,720^AUN,50,50^FD" + Texto(envio.NombrePlaza) + "^FS");
                Linea(zpl, "^FO300,770^AUN,50,50^FD" + Texto(envio.CodPostal) + "^FS");
                Linea(zpl, "^FO600,770^AUN,50,50^FD" + i.ToString(CultureInfo.InvariantCulture) + "/" + bultos + "^FS");
                Linea(zpl, "^FO300,820^GB775,0,2,B,3^FS");
                Linea(zpl, "^FO10,300^B2B,200,Y,N,Y^FD" + codigoBarras + i.ToString("D3", CultureInfo.InvariantCulture) + "^FS");
                Linea(zpl, "^FO10,860^ADN,1,1^FDAlb.Cli.^FS");
                Linea(zpl, "^FO110,850^ASN,1,1^FD" + cliente + pedido + "^FS");
                Linea(zpl, "^FO10,900^ADN,1,1^FDRef.Cli.^FS");
                Linea(zpl, "^FO110,890^ASN,1,1^FD" + cliente + "/" + pedido + "^FS");
                Linea(zpl, "^XZ");
            }
            return zpl.ToString();
        }

        /// <summary>
        /// La ZPL en base64 con los bytes en ANSI 1252 (^CI27), que es como la lee AgenciaGestionadaPorApi.ImprimirEtiquetaZpl
        /// de Nesto (Encoding.Default) y como la vuelve a escribir ImpresoraEtiquetasWindows.
        /// </summary>
        public static string EnBase64(string zpl) => Convert.ToBase64String(Encoding.GetEncoding(1252).GetBytes(zpl ?? string.Empty));

        // Los campos char de la BD llegan con relleno: se quitan (Nesto los mandaba tal cual; los espacios del final no se ven).
        // «^» y «~» son órdenes de ZPL: un dato con ellos rompería la etiqueta.
        private static string Texto(string valor) => (valor ?? string.Empty).Replace('^', ' ').Replace('~', ' ').Trim();

        private static void Linea(StringBuilder zpl, string linea) => zpl.Append(linea).Append("\r\n");
    }

    /// <summary>NestoAPI#552 (fase 4): la etiqueta de GLS lista para mandar a la Zebra.</summary>
    public class EtiquetaGlsGenerada
    {
        public string Zpl { get; set; }
        public string CodigoTsp { get; set; }
        /// <summary>True si el envío no tenía plaza y se ha pedido a GLS (GetPlazaXCP) para la etiqueta.</summary>
        public bool PlazaConsultada { get; set; }
    }

    /// <summary>
    /// NestoAPI#552 (fase 4): genera la etiqueta de un envío de GLS sin grabarlo en GLS. Usa la plaza y el código de barras que
    /// el envío ya tiene (los deterministas de <see cref="PlazaYCodigoBarrasGls"/>, calculados al crearlo); solo si el envío no
    /// tiene plaza se le pide a GLS (consulta, como Nesto en cada impresión). El código de ruta (TSP) se consulta a GLS como
    /// hace Nesto: si GLS no responde, no hay etiqueta (como en Nesto) y se lanza <see cref="AgenciaRemotaException"/>.
    /// </summary>
    public class GeneradorEtiquetaGls
    {
        private readonly IClienteTspGls _tsp;
        private readonly IPlazaYCodigoBarrasAgencia _plaza;

        public GeneradorEtiquetaGls(IClienteTspGls tsp, IPlazaYCodigoBarrasAgencia plaza)
        {
            _tsp = tsp ?? throw new ArgumentNullException(nameof(tsp));
            _plaza = plaza;
        }

        /// <param name="prefijoAgencia">AgenciasTransporte.PrefijoCodigoBarras, por si el envío no tiene código (se calcula como AgenciaASM).</param>
        public async Task<EtiquetaGlsGenerada> GenerarAsync(EnviosAgencia envio, string prefijoAgencia)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));

            if (string.IsNullOrWhiteSpace(envio.CodigoBarras))
            {
                envio.CodigoBarras = PlazaYCodigoBarrasGls.CodigoBarras(envio, prefijoAgencia)
                    ?? throw new InvalidOperationException($"El envío {envio.Numero} no tiene código de barras y la agencia no tiene prefijo para calcularlo.");
            }

            bool plazaConsultada = false;
            if (string.IsNullOrWhiteSpace(envio.Nemonico) && _plaza != null)
            {
                PlazaAgencia plaza = await _plaza.CalcularPlazaAsync(envio.CodPostal, envio.Pais).ConfigureAwait(false);
                if (plaza != null && !plaza.EsVacia)
                {
                    envio.Nemonico = plaza.Nemonico;
                    envio.NombrePlaza = plaza.Nombre;
                    envio.TelefonoPlaza = plaza.Telefono;
                    envio.EmailPlaza = plaza.Email;
                    plazaConsultada = true;
                }
            }

            string respuesta = await _tsp.EnviarAsync(ClienteTspGls.ConstruirSoap(envio)).ConfigureAwait(false);
            string tsp = RespuestaTspGls.Interpretar(respuesta);
            // NestoAPI#552 (decisión de Carlos del 27/10/26): sin código de ruta GLS no entrega el paquete, así que nunca se
            // imprime sin él. Nesto imprimía «N/A» o «ERROR» en su lugar; la API no.
            if (!RespuestaTspGls.EsCodigoValido(tsp))
            {
                throw new AgenciaRemotaException(
                    $"GLS no ha dado el código de ruta (TSP) del envío {envio.Numero} (contesta «{tsp}»): sin él no se imprime la etiqueta.")
                {
                    EsTransitoria = true
                };
            }

            return new EtiquetaGlsGenerada
            {
                Zpl = EtiquetaGls.ConstruirZpl(envio, tsp),
                CodigoTsp = tsp,
                PlazaConsultada = plazaConsultada
            };
        }
    }
}
