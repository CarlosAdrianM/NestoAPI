using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Verifactu
{
    /// <summary>
    /// NestoAPI#599: construye el IDOtro del destinatario de una factura a Verifactu a partir del tipo
    /// (catálogo L7) y el país que decide <c>ServicioValidacionNif</c> (marca manual o país fiscal de la
    /// ficha, <c>Clientes.Pais</c>) y del identificador que lleva la factura.
    ///
    /// Caso real (06/10/26, NV2616366, cliente 41990 de Vila do Conde): la ficha decía PT y el NIF era
    /// «311482473». Ya se declaraba con IDOtro 02 y país PT, pero con el número PELADO: un NIF-IVA es
    /// SIEMPRE prefijo de país + número, y Verifacti lo rechazaba («El IVA (311482473) no tiene un formato
    /// valido»). En la ficha es normal que el NIF portugués (o el de cualquier país) venga sin prefijo.
    ///
    /// Regla (solo toca el tipo 02, NIF-IVA, de un país de la UE; el resto pasa tal cual):
    /// - Ya lleva el prefijo VIES de su país (PT311482473, EL123456789 para Grecia) → 02 sin duplicarlo.
    /// - Sin prefijo pero con el formato del NIF-IVA de su país (PT: 9 dígitos) → 02 con el prefijo añadido.
    /// - Con el prefijo de OTRO país de la UE (ficha PT con «FR...») → 02 tal cual y CodigoPais el del
    ///   prefijo: la AEAT exige que el país del IDOtro 02 case con el del NIF-IVA.
    /// - Si no parece un NIF-IVA → 04 (documento oficial de identificación del país): con 02 el rechazo
    ///   sería seguro; el 04 no pasa por VIES ni exige formato de NIF-IVA.
    /// Los demás tipos no cambian: 07 «no censado» de #391 (país ES), 03 pasaporte, 04 de los clientes de
    /// fuera de la UE (#584) y el 02→04 de las ventas OSS (#375), que se decide antes en ServicioFacturas.
    /// Pura y estática para testearla sin BD.
    /// </summary>
    internal static class IdentificacionDestinatarioVerifactu
    {
        internal const string TIPO_NIF_IVA = "02";
        internal const string TIPO_DOC_OFICIAL_PAIS = "04";

        /// <summary>Formato del número de NIF-IVA SIN prefijo por país (ISO-2), según VIES. Grecia usa el
        /// prefijo EL (ISO GR).</summary>
        private static readonly Dictionary<string, Regex> _formatoNifIvaPorPais = new Dictionary<string, Regex>
        {
            ["AT"] = Formato(@"U\d{8}"),
            ["BE"] = Formato(@"[01]?\d{9}"),
            ["BG"] = Formato(@"\d{9,10}"),
            ["CY"] = Formato(@"\d{8}[A-Z]"),
            ["CZ"] = Formato(@"\d{8,10}"),
            ["DE"] = Formato(@"\d{9}"),
            ["DK"] = Formato(@"\d{8}"),
            ["EE"] = Formato(@"\d{9}"),
            ["GR"] = Formato(@"\d{9}"),
            ["FI"] = Formato(@"\d{8}"),
            ["FR"] = Formato(@"[0-9A-Z]{2}\d{9}"),
            ["HR"] = Formato(@"\d{11}"),
            ["HU"] = Formato(@"\d{8}"),
            ["IE"] = Formato(@"\d{7}[A-Z]{1,2}|\d[A-Z+*]\d{5}[A-Z]"),
            ["IT"] = Formato(@"\d{11}"),
            ["LT"] = Formato(@"\d{9}|\d{12}"),
            ["LU"] = Formato(@"\d{8}"),
            ["LV"] = Formato(@"\d{11}"),
            ["MT"] = Formato(@"\d{8}"),
            ["NL"] = Formato(@"\d{9}B\d{2}"),
            ["PL"] = Formato(@"\d{10}"),
            ["PT"] = Formato(@"\d{9}"),
            ["RO"] = Formato(@"\d{2,10}"),
            ["SE"] = Formato(@"\d{12}"),
            ["SI"] = Formato(@"\d{8}"),
            ["SK"] = Formato(@"\d{10}")
        };

        private static Regex Formato(string patron) => new Regex("^(?:" + patron + ")$", RegexOptions.Compiled);

        /// <summary>Prefijo VIES del NIF-IVA de un país (ISO-2): igual salvo Grecia (EL).</summary>
        internal static string PrefijoNifIva(string pais) => pais == "GR" ? "EL" : pais;

        internal static VerifactuIdOtro Construir(string tipoIdentificacion, string pais, string identificador)
        {
            string tipo = tipoIdentificacion?.Trim();
            string codigoPais = pais?.Trim().ToUpperInvariant();
            string id = identificador?.Trim();
            var idOtro = new VerifactuIdOtro { IdType = tipo, CodigoPais = codigoPais, Id = id };

            if (tipo != TIPO_NIF_IVA || string.IsNullOrEmpty(id) || string.IsNullOrEmpty(codigoPais)
                || !_formatoNifIvaPorPais.ContainsKey(codigoPais))
            {
                return idOtro;
            }

            // Sin separadores ni minúsculas (la ficha puede traer "pt 311 482 473" o "311.482.473")
            string compacto = new string(id.ToUpperInvariant().Where(c => !char.IsWhiteSpace(c) && c != '.' && c != '-').ToArray());

            // 1. Prefijo de su propio país
            string prefijo = PrefijoNifIva(codigoPais);
            if (compacto.StartsWith(prefijo) && _formatoNifIvaPorPais[codigoPais].IsMatch(compacto.Substring(prefijo.Length)))
            {
                idOtro.Id = compacto;
                return idOtro;
            }
            // 2. Prefijo de otro país de la UE con su formato: el país del IDOtro es el del prefijo
            string paisDelPrefijo = PaisDelPrefijo(compacto);
            if (paisDelPrefijo != null && _formatoNifIvaPorPais[paisDelPrefijo].IsMatch(compacto.Substring(2)))
            {
                idOtro.Id = compacto;
                idOtro.CodigoPais = paisDelPrefijo;
                return idOtro;
            }
            // 3. Número pelado con el formato del NIF-IVA de su país: se le pone el prefijo
            if (_formatoNifIvaPorPais[codigoPais].IsMatch(compacto))
            {
                idOtro.Id = prefijo + compacto;
                return idOtro;
            }
            // 4. No es un NIF-IVA: documento oficial del país, con el identificador tal cual
            idOtro.IdType = TIPO_DOC_OFICIAL_PAIS;
            return idOtro;
        }

        private static string PaisDelPrefijo(string compacto)
        {
            if (compacto.Length < 3)
            {
                return null;
            }
            string prefijo = compacto.Substring(0, 2);
            if (prefijo == "GR")
            {
                return null; // el NIF-IVA griego va con EL, no con su ISO
            }
            string pais = prefijo == "EL" ? "GR" : prefijo;
            return _formatoNifIvaPorPais.ContainsKey(pais) ? pais : null;
        }
    }
}
