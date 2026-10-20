using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using NestoAPI.Models;

namespace NestoAPI.Infraestructure.Agencias.Gls
{
    /// <summary>NestoAPI#595 (slice 2, 20/10/26): el transporte de GetPlazaXCP. Se fakea en los tests.</summary>
    public interface IClientePlazaGls : IClienteSoapGls
    {
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): GetPlazaXCP del WS b2b de GLS, como AgenciaASM.calcularPlaza de Nesto: mismo
    /// SOAPAction y 15 s de espera como mucho (Nesto: Timeout y ReadWriteTimeout de 15000).
    /// </summary>
    public class ClientePlazaGls : ClienteSoapGls, IClientePlazaGls
    {
        public const string OPERACION_GET_PLAZA = "GetPlazaXCP";
        public const string URL_GET_PLAZA = "https://www.asmred.com/WebSrvs/b2b.asmx?op=" + OPERACION_GET_PLAZA;
        public static readonly TimeSpan ESPERA_MAXIMA = TimeSpan.FromSeconds(15);

        private static readonly HttpClient Cliente15s = new HttpClient { Timeout = ESPERA_MAXIMA };

        public ClientePlazaGls(HttpClient http = null)
            : base(OPERACION_GET_PLAZA, URL_GET_PLAZA, http ?? Cliente15s)
        {
        }

        /// <summary>El sobre de AgenciaASM.calcularPlaza (codPais y cp directamente dentro de GetPlazaXCP, sin docIn).</summary>
        public static string ConstruirSoap(string codPostal, int pais)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<soap:Envelope xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                "xmlns:xsd=\"http://www.w3.org/2001/XMLSchema\" " +
                "xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
                "<soap:Body>" +
                "<" + OPERACION_GET_PLAZA + " xmlns=\"http://www.asmred.com/\">" +
                "<codPais>" + pais.ToString(CultureInfo.InvariantCulture) + "</codPais>" +
                "<cp>" + SecurityElement.Escape(codPostal?.Trim() ?? string.Empty) + "</cp>" +
                "</" + OPERACION_GET_PLAZA + ">" +
                "</soap:Body>" +
                "</soap:Envelope>";
        }
    }

    /// <summary>NestoAPI#595 (slice 2, 20/10/26): lectura de la respuesta de GetPlazaXCP, como AgenciaASM.calcularPlaza.</summary>
    public static class RespuestaPlazaGls
    {
        private static readonly Regex NoCifras = new Regex("[^0-9]", RegexOptions.Compiled);

        /// <summary>
        /// La plaza del primer nodo de GetPlazaXCPResult (Nemonico, Nombre, Telefono solo con cifras y recortado a 27, Mail).
        /// Vacía si la respuesta no se entiende o no trae Nemonico (Nesto deja los campos como estaban). Los hijos se buscan
        /// por nombre local, con o sin espacio de nombres.
        /// </summary>
        public static PlazaAgencia Interpretar(string respuesta)
        {
            if (string.IsNullOrWhiteSpace(respuesta))
            {
                return PlazaAgencia.Vacia();
            }

            XDocument documento;
            try
            {
                documento = XDocument.Parse(respuesta.TrimStart('﻿', ' ', '\r', '\n', '\t'));
            }
            catch (XmlException)
            {
                return PlazaAgencia.Vacia();
            }

            XElement resultado = documento.Descendants(PeticionGls.Ns + (ClientePlazaGls.OPERACION_GET_PLAZA + "Result")).FirstOrDefault();
            XElement plaza = resultado?.Elements().FirstOrDefault();
            string nemonico = Hijo(plaza, "Nemonico");
            if (nemonico == null)
            {
                return PlazaAgencia.Vacia();
            }

            return new PlazaAgencia
            {
                Nemonico = nemonico,
                Nombre = Hijo(plaza, "Nombre") ?? string.Empty,
                Telefono = PlazaAgencia.RecortarTelefono(NoCifras.Replace(Hijo(plaza, "Telefono") ?? string.Empty, string.Empty)),
                Email = Hijo(plaza, "Mail") ?? string.Empty
            };
        }

        private static string Hijo(XElement padre, string nombre)
            => padre?.Elements().FirstOrDefault(e => e.Name.LocalName == nombre)?.Value?.Trim();
    }

    /// <summary>
    /// NestoAPI#595 (slice 2, 20/10/26): GLS/ASM (agencia 1 y la de la empresa espejo, 5), como AgenciaASM.vb. La plaza se pide
    /// a GLS (GetPlazaXCP, l.148-220) y, si falla, queda vacía sin lanzar (Nesto es silencioso; aquí además va a ELMAH). El
    /// código de barras es prefijo + número del envío con 7 cifras (l.142-147), con el prefijo propio de BusinessParcel
    /// (servicio 96).
    /// </summary>
    public class PlazaYCodigoBarrasGls : IPlazaYCodigoBarrasAgencia
    {
        /// <summary>Prefijo del código de barras de BusinessParcel (servicio 96), que tiene su propia cuenta (AgenciaASM).</summary>
        public const string PREFIJO_CODIGO_BARRAS_BUSINESSPARCEL = "6119714";

        private readonly IClientePlazaGls _cliente;
        private readonly Action<Exception> _registrar;

        /// <param name="registrar">Dónde se registran los fallos de la plaza (null = ELMAH).</param>
        public PlazaYCodigoBarrasGls(IClientePlazaGls cliente, Action<Exception> registrar = null)
        {
            _cliente = cliente ?? throw new ArgumentNullException(nameof(cliente));
            _registrar = registrar ?? (ex => ElmahHelper.Log(ex));
        }

        public IReadOnlyCollection<int> Agencias { get; } = new[]
        {
            Constantes.Agencias.AGENCIA_GLS, Constantes.Agencias.AGENCIA_GLS_EMPRESA_ESPEJO
        };

        public bool PlazaConsultaLaAgencia => true;

        public async Task<PlazaAgencia> CalcularPlazaAsync(string codPostal, int pais)
        {
            try
            {
                string respuesta = await _cliente.EnviarAsync(ClientePlazaGls.ConstruirSoap(codPostal, pais)).ConfigureAwait(false);
                return RespuestaPlazaGls.Interpretar(respuesta);
            }
            catch (Exception ex)
            {
                try
                {
                    _registrar(new Exception($"[Plaza GLS #595] No se pudo pedir la plaza del CP {codPostal?.Trim()} (país {pais}) a GLS: {ex.Message}", ex));
                }
                catch
                {
                    // El registro nunca rompe el cálculo de la plaza.
                }
                return PlazaAgencia.Vacia();
            }
        }

        public string CalcularCodigoBarras(EnviosAgencia envio, string prefijoAgencia) => CodigoBarras(envio, prefijoAgencia);

        /// <summary>
        /// El código de barras de un envío de GLS, como AgenciaASM.calcularCodigoBarras: prefijo (el de BusinessParcel con el
        /// servicio 96; si no, AgenciasTransporte.PrefijoCodigoBarras) + número del envío con 7 cifras. Null si no hay prefijo.
        /// </summary>
        public static string CodigoBarras(EnviosAgencia envio, string prefijoAgencia)
        {
            if (envio == null) throw new ArgumentNullException(nameof(envio));
            string prefijo = envio.Servicio == PeticionGls.SERVICIO_BUSINESSPARCEL ? PREFIJO_CODIGO_BARRAS_BUSINESSPARCEL : prefijoAgencia?.Trim();
            return string.IsNullOrEmpty(prefijo) ? null : prefijo + envio.Numero.ToString("D7", CultureInfo.InvariantCulture);
        }
    }
}
