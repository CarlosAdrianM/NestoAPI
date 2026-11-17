using NestoAPI.Models.Clientes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NestoAPI.Infraestructure.Clientes
{
    /// <summary>
    /// NestoAPI#874: qué es la dirección de entrega (Clientes.TipoLugar). Los valores son los del CHECK
    /// CK_Clientes_TipoLugar de Scripts/Issue874_ClientesDireccion2.sql: si se añade uno, hay que cambiar la restricción.
    /// </summary>
    public static class TiposLugar
    {
        public const string PISO = "Piso";
        public const string LOCAL = "Local";
        public const string CASA = "Casa";
        public const string NAVE = "Nave";
        public const string OTRO = "Otro";

        public static readonly IReadOnlyList<string> TODOS = new[] { PISO, LOCAL, CASA, NAVE, OTRO };

        /// <summary>El valor canónico («local» → «Local»); null si no es ninguno.</summary>
        public static string Normalizar(string tipo)
        {
            string limpio = tipo?.Trim();
            return string.IsNullOrEmpty(limpio)
                ? null
                : TODOS.FirstOrDefault(t => string.Equals(t, limpio, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>NestoAPI#874: el resultado de componer la Dirección 2 del alta.</summary>
    public sealed class ResultadoDireccion2
    {
        public bool Valida { get; private set; }
        /// <summary>Por qué no vale (de usted); null si vale.</summary>
        public string Motivo { get; private set; }
        /// <summary>El tipo canónico (Piso, Local…); null si no hay.</summary>
        public string TipoLugar { get; private set; }
        /// <summary>Lo que se guarda en Clientes.Direccion2, en mayúsculas («1º D», «LOCAL 6 · RÓTULO: EL EDÉN»).</summary>
        public string Direccion2 { get; private set; }

        internal static ResultadoDireccion2 Bien(string tipo, string direccion2)
            => new ResultadoDireccion2 { Valida = true, TipoLugar = tipo, Direccion2 = direccion2 };

        internal static ResultadoDireccion2 Mal(string tipo, string motivo)
            => new ResultadoDireccion2 { Valida = false, TipoLugar = tipo, Motivo = motivo };

        /// <summary>Sin tipo de lugar y sin exigirlo: no hay nada que guardar.</summary>
        internal static readonly ResultadoDireccion2 Nada = new ResultadoDireccion2 { Valida = true };
    }

    /// <summary>
    /// NestoAPI#874 (Carlos 17/11/26): la «Dirección 2» del cliente, lo que necesita el repartidor para encontrarlo y que la
    /// calle de Google no dice. Regla PURA y única (alta de Nesto y, con su gemela, NestoApp#286):
    /// <list type="bullet">
    /// <item>Piso: piso y puerta obligatorios → «1º D».</item>
    /// <item>Local: número de local opcional y RÓTULO obligatorio → «LOCAL 6 · RÓTULO: EL EDÉN» (o «LOCAL · RÓTULO: EL EDÉN»).
    /// El rótulo no se propone a partir del nombre del cliente: el fiscal no es lo que pone en la puerta.</item>
    /// <item>Casa / chalet → «CASA».</item>
    /// <item>Nave / polígono: número de nave obligatorio → «NAVE 4».</item>
    /// <item>Otro: texto obligatorio, tal cual en mayúsculas.</item>
    /// </list>
    /// Clientes.Dirección sigue siendo solo calle y número (50); esto va en Clientes.Direccion2 (nvarchar(100)).
    /// </summary>
    public static class Direccion2Cliente
    {
        public const int LONGITUD = 100;
        internal const string SEPARADOR_ROTULO = " · ";

        private static readonly CultureInfo ESPANOL = new CultureInfo("es-ES");
        private static readonly Regex PISO_NUMERICO = new Regex(@"^(\d+)\s*[ºª°oO]?$", RegexOptions.Compiled);
        private static readonly Regex ESPACIOS = new Regex(@"\s+", RegexOptions.Compiled);

        public const string MOTIVO_SIN_TIPO = "Indique qué es la dirección (piso, local, casa o chalet, nave u otro): " +
            "sin eso el repartidor no la encuentra.";
        public const string MOTIVO_PISO = "En un piso indique el piso y la puerta (por ejemplo, 1º D).";
        public const string MOTIVO_LOCAL = "En un local indique qué pone en el rótulo de la entrada: es lo que busca el repartidor.";
        public const string MOTIVO_NAVE = "En una nave indique su número.";
        public const string MOTIVO_OTRO = "Describa el lugar en «Otro» (por ejemplo, «CENTRO COMERCIAL, PLANTA 2»).";

        public static string MotivoTipoDesconocido(string tipo)
            => $"«{tipo?.Trim()}» no es un tipo de lugar válido: tiene que ser {string.Join(", ", TiposLugar.TODOS.Take(TiposLugar.TODOS.Count - 1))} u {TiposLugar.TODOS.Last()}.";

        public static string MotivoDemasiadoLarga(string direccion2)
            => $"La dirección 2 («{direccion2}») no puede superar {LONGITUD} caracteres: acórtela.";

        /// <summary>Compone y valida. Un tipo vacío no es válido (quien no exige el tipo no llama aquí: <see cref="ParaDireccion"/>).</summary>
        public static ResultadoDireccion2 Componer(string tipoLugar, string piso, string puerta, string numeroLocal, string rotulo,
            string numeroNave, string otroTexto)
        {
            if (string.IsNullOrWhiteSpace(tipoLugar))
            {
                return ResultadoDireccion2.Mal(null, MOTIVO_SIN_TIPO);
            }
            string tipo = TiposLugar.Normalizar(tipoLugar);
            if (tipo == null)
            {
                return ResultadoDireccion2.Mal(null, MotivoTipoDesconocido(tipoLugar));
            }

            string texto;
            switch (tipo)
            {
                case TiposLugar.PISO:
                    string elPiso = Limpiar(piso), laPuerta = Limpiar(puerta);
                    if (elPiso == null || laPuerta == null)
                    {
                        return ResultadoDireccion2.Mal(tipo, MOTIVO_PISO);
                    }
                    Match numerico = PISO_NUMERICO.Match(elPiso);
                    texto = (numerico.Success ? numerico.Groups[1].Value + "º" : elPiso) + " " + laPuerta;
                    break;
                case TiposLugar.LOCAL:
                    string elRotulo = Limpiar(rotulo);
                    if (elRotulo == null)
                    {
                        return ResultadoDireccion2.Mal(tipo, MOTIVO_LOCAL);
                    }
                    string elLocal = Limpiar(numeroLocal);
                    texto = (elLocal == null ? "LOCAL" : "LOCAL " + elLocal) + SEPARADOR_ROTULO + "RÓTULO: " + elRotulo;
                    break;
                case TiposLugar.CASA:
                    texto = "CASA";
                    break;
                case TiposLugar.NAVE:
                    string laNave = Limpiar(numeroNave);
                    if (laNave == null)
                    {
                        return ResultadoDireccion2.Mal(tipo, MOTIVO_NAVE);
                    }
                    texto = "NAVE " + laNave;
                    break;
                default: // Otro
                    string elTexto = Limpiar(otroTexto);
                    if (elTexto == null)
                    {
                        return ResultadoDireccion2.Mal(tipo, MOTIVO_OTRO);
                    }
                    texto = elTexto;
                    break;
            }

            return texto.Length > LONGITUD
                ? ResultadoDireccion2.Mal(tipo, MotivoDemasiadoLarga(texto))
                : ResultadoDireccion2.Bien(tipo, texto);
        }

        public static ResultadoDireccion2 Componer(ClienteCrear c)
            => c == null
                ? ResultadoDireccion2.Mal(null, MOTIVO_SIN_TIPO)
                : Componer(c.TipoLugar, c.Piso, c.Puerta, c.NumeroLocal, c.Rotulo, c.NumeroNave, c.OtroTexto);

        /// <summary>
        /// Lo que decide quien guarda una dirección (hoy el alta; Carlos 17/11/26: también lo hará la modificación cuando CAMBIE
        /// la dirección, con este mismo método): sin dirección no hay nada que decir (contactos de cobro); sin tipo de lugar,
        /// solo se rechaza si <paramref name="exigir"/> (en el alta, ParametrosUsuario «(defecto)» ExigirDireccion2Alta = 1);
        /// con tipo, se valida siempre (quien lo manda ya conoce la regla). Nunca null: <see cref="ResultadoDireccion2.Valida"/>.
        /// </summary>
        public static ResultadoDireccion2 ParaDireccion(ClienteCrear c, bool exigir)
        {
            if (c == null || string.IsNullOrWhiteSpace(c.Direccion))
            {
                return ResultadoDireccion2.Nada;
            }
            if (string.IsNullOrWhiteSpace(c.TipoLugar) && !exigir)
            {
                return ResultadoDireccion2.Nada;
            }
            return Componer(c);
        }

        private static string Limpiar(string texto)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return null;
            }
            return ESPACIOS.Replace(texto.Trim(), " ").ToUpper(ESPANOL);
        }
    }
}
