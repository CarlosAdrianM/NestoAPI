using System;
using System.Configuration;

namespace NestoAPI.Infraestructure.Abandono
{
    /// <summary>
    /// NestoAPI#714: el interruptor <c>Abandono:Modo</c> del Web.config. Sin clave o con cualquier otro valor, Apagado (los jobs
    /// no hacen nada). Sombra (nace así, corte 1): se evalúa la regla cada noche, se registra en ClientesRiesgoAbandono y se
    /// resuelve al vencer el horizonte, sin avisar a nadie ni cambiar nada visible. Activo (corte 4): además, origen ABANDONO en
    /// las llamadas sugeridas y aviso semanal; en el corte 1 se comporta como Sombra.
    /// </summary>
    public enum ModoAbandono
    {
        Apagado,
        Sombra,
        Activo
    }

    public static class InterruptorAbandono
    {
        public const string CLAVE = "Abandono:Modo";

        /// <summary>Para los tests (null = el Web.config).</summary>
        internal static Func<string, string> LeerAjuste { get; set; }

        public static ModoAbandono Modo => Interpretar(Leer(CLAVE));

        internal static ModoAbandono Interpretar(string valor)
        {
            switch (valor?.Trim().ToUpperInvariant())
            {
                case "SOMBRA":
                    return ModoAbandono.Sombra;
                case "ACTIVO":
                    return ModoAbandono.Activo;
                default:
                    return ModoAbandono.Apagado;
            }
        }

        private static string Leer(string clave)
        {
            try
            {
                return LeerAjuste != null ? LeerAjuste(clave) : ConfigurationManager.AppSettings[clave];
            }
            catch
            {
                return null;
            }
        }
    }
}
