using NestoAPI.Models;
using System;
using System.Globalization;

namespace NestoAPI.Infraestructure.CorreosPostCompra
{
    /// <summary>NestoAPI#736: nivel de la puerta del correo de reposición con el modelo 1 de propensión.</summary>
    public enum ModoPuertaModeloReposicion
    {
        /// <summary>No se puntúa: como antes de #736.</summary>
        Apagado,
        /// <summary>Se puntúa y se registra la P1 y si habría quedado fuera, sin quitar nada. Es el valor por defecto.</summary>
        Sombra,
        /// <summary>Se quitan (a Descartes, con motivo) los de P1 menor que el umbral.</summary>
        Activo
    }

    /// <summary>
    /// NestoAPI#736: los parámetros de «(defecto)» de las mejoras del recordatorio de reposición. Todos se leen sin lanzar: un
    /// fallo al leer cuenta como «sin fila».
    /// </summary>
    public static class ParametrosRecordatorioReposicion
    {
        private const string EMPRESA = Constantes.Empresas.EMPRESA_POR_DEFECTO;
        public const decimal UMBRAL_MODELO_POR_DEFECTO = 0.15m;

        /// <summary>Tope de días desde la última compra. Null = sin tope.</summary>
        public static int? LeerTopeDias(ILectorParametrosUsuario lector)
            => InterpretarTopeDias(Leer(lector, Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_TOPE_DIAS, out bool error), error);

        /// <summary>
        /// Sin fila (null) o error: <see cref="CalculadoraReposicion.TOPE_DIAS_POR_DEFECTO"/>. «0», vacío o negativo: sin tope
        /// (null). Un número: ese. Cualquier otra cosa: el de por defecto.
        /// </summary>
        public static int? InterpretarTopeDias(string valor, bool error = false)
        {
            if (error || valor == null)
            {
                return CalculadoraReposicion.TOPE_DIAS_POR_DEFECTO;
            }
            string limpio = valor.Trim();
            if (limpio.Length == 0)
            {
                return null;
            }
            if (!int.TryParse(limpio, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dias))
            {
                return CalculadoraReposicion.TOPE_DIAS_POR_DEFECTO;
            }
            return dias <= 0 ? (int?)null : dias;
        }

        public static ModoPuertaModeloReposicion LeerModoPuerta(ILectorParametrosUsuario lector)
            => InterpretarModoPuerta(Leer(lector, Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_PUERTA_MODELO, out _));

        /// <summary>«Activo» y «Apagado» (o «0»); cualquier otra cosa, sin fila incluida: Sombra.</summary>
        public static ModoPuertaModeloReposicion InterpretarModoPuerta(string valor)
        {
            string limpio = valor?.Trim();
            if (string.Equals(limpio, "Activo", StringComparison.OrdinalIgnoreCase))
            {
                return ModoPuertaModeloReposicion.Activo;
            }
            if (string.Equals(limpio, "Apagado", StringComparison.OrdinalIgnoreCase) || limpio == "0")
            {
                return ModoPuertaModeloReposicion.Apagado;
            }
            return ModoPuertaModeloReposicion.Sombra;
        }

        public static decimal LeerUmbralModelo(ILectorParametrosUsuario lector)
            => InterpretarUmbralModelo(Leer(lector, Constantes.ParametrosUsuario.RECORDATORIO_REPOSICION_UMBRAL_MODELO, out _));

        /// <summary>«0,15» o «0.15», entre 0 y 1. Sin fila, vacío o fuera de rango: <see cref="UMBRAL_MODELO_POR_DEFECTO"/>.</summary>
        public static decimal InterpretarUmbralModelo(string valor)
        {
            string limpio = valor?.Trim().Replace(',', '.');
            if (string.IsNullOrEmpty(limpio)
                || !decimal.TryParse(limpio, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal umbral)
                || umbral < 0 || umbral > 1)
            {
                return UMBRAL_MODELO_POR_DEFECTO;
            }
            return umbral;
        }

        private static string Leer(ILectorParametrosUsuario lector, string clave, out bool error)
        {
            error = false;
            if (lector == null)
            {
                return null;
            }
            try
            {
                return lector.LeerParametro(EMPRESA, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, clave);
            }
            catch
            {
                error = true;
                return null;
            }
        }
    }
}
