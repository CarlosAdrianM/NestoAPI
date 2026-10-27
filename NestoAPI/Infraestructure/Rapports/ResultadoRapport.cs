using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace NestoAPI.Infraestructure.Rapports
{
    /// <summary>
    /// NestoAPI#716 (2.1): resultado del rapport y motivo de no compra. Catálogo cerrado (no texto libre) para que los
    /// modelos de la épica #689 (abandono, llamadas, campañas) puedan aprender de él. Se guarda en
    /// <c>SeguimientoCliente.Resultado</c> (varchar(20)) junto con <c>ProximoContacto</c> (date): los dos opcionales,
    /// los clientes viejos que no los mandan siguen igual.
    /// </summary>
    public static class ResultadoRapport
    {
        public const string PEDIDO = "Pedido";
        public const string PEDIRA_MAS_TARDE = "PediraMasTarde";
        public const string NO_NECESITA = "NoNecesita";
        public const string PRECIO = "Precio";
        public const string COMPETENCIA = "Competencia";
        public const string CERRADO_TRASPASO = "CerradoTraspaso";
        public const string SIN_STOCK = "SinStock";
        public const string NO_DECIDE = "NoDecide";
        public const string SOLO_INFORMACION = "SoloInformacion";
        public const string OTRO = "Otro";

        /// <summary>Longitud de la columna SeguimientoCliente.Resultado.</summary>
        public const int LONGITUD_MAXIMA = 20;

        /// <summary>Código → nombre para mostrar (chips de Nesto y NestoApp).</summary>
        public static readonly IReadOnlyDictionary<string, string> Nombres = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { PEDIDO, "Pedido" },
            { PEDIRA_MAS_TARDE, "Pedirá más tarde" },
            { NO_NECESITA, "No necesita" },
            { PRECIO, "Precio" },
            { COMPETENCIA, "Competencia" },
            { CERRADO_TRASPASO, "Cerrado / traspaso" },
            { SIN_STOCK, "Sin stock" },
            { NO_DECIDE, "No decide" },
            { SOLO_INFORMACION, "Solo información" },
            { OTRO, "Otro" }
        };

        /// <summary>Los códigos, en el orden en que se pintan.</summary>
        public static readonly IReadOnlyList<string> Codigos = new[]
        {
            PEDIDO, PEDIRA_MAS_TARDE, NO_NECESITA, PRECIO, COMPETENCIA, CERRADO_TRASPASO, SIN_STOCK, NO_DECIDE, SOLO_INFORMACION, OTRO
        };

        public static bool EsValido(string resultado)
        {
            return !string.IsNullOrWhiteSpace(resultado) && Nombres.ContainsKey(resultado.Trim());
        }

        /// <summary>
        /// Deja el código como se guarda: null si no viene (opcional), o el código exacto del catálogo. Admite el código
        /// sin distinguir mayúsculas ni tildes («pedira mas tarde», «CERRADOTRASPASO») para los clientes que lo tecleen;
        /// lo que no case devuelve null y <see cref="MotivoParaNoGuardar"/> lo explica.
        /// </summary>
        public static string Normalizar(string resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado))
            {
                return null;
            }
            string clave = Simplificar(resultado);
            return Codigos.FirstOrDefault(c => Simplificar(c) == clave || Simplificar(Nombres[c]) == clave);
        }

        /// <summary>Null si no hay nada que objetar (también cuando no viene); si no, el motivo para el cliente (400).</summary>
        public static string MotivoParaNoGuardar(string resultado)
        {
            if (string.IsNullOrWhiteSpace(resultado) || Normalizar(resultado) != null)
            {
                return null;
            }
            return $"El resultado del rapport '{resultado.Trim()}' no está en el catálogo. Valores admitidos: {string.Join(", ", Codigos)}.";
        }

        /// <summary>Minúsculas, sin tildes, sin espacios ni signos: para comparar códigos y nombres.</summary>
        internal static string Simplificar(string texto)
        {
            if (texto == null)
            {
                return string.Empty;
            }
            string descompuesto = texto.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(descompuesto.Length);
            foreach (char c in descompuesto)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }
                if (char.IsLetterOrDigit(c))
                {
                    _ = sb.Append(char.ToLowerInvariant(c));
                }
            }
            return sb.ToString();
        }
    }
}
