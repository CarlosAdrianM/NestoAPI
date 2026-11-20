using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NestoAPI.Models;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias
{
    /// <summary>
    /// Carlos 20/11/26 (envío 251227, borrado con la etiqueta ya pegada y sin rastro): borrar un envío deja SIEMPRE una fila en
    /// Modificaciones (Tabla <see cref="TABLA"/>) con el envío ENTERO y la historia que se borra con él, en la misma transacción, de
    /// cualquier agencia y estado. PURA.
    /// </summary>
    public static class RastroBorradoEnvio
    {
        public const string TABLA = "EnvioAgenciaBorrado";

        /// <summary>Solo las columnas (sin navegaciones): todo lo que es valor, texto o binario.</summary>
        public static Dictionary<string, object> Columnas(EnviosAgencia envio)
        {
            var columnas = new Dictionary<string, object>();
            if (envio == null)
            {
                return columnas;
            }
            foreach (PropertyInfo propiedad in typeof(EnviosAgencia).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
                .OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                Type tipo = Nullable.GetUnderlyingType(propiedad.PropertyType) ?? propiedad.PropertyType;
                if (tipo.IsPrimitive || tipo.IsEnum || tipo == typeof(string) || tipo == typeof(decimal) || tipo == typeof(DateTime)
                    || tipo == typeof(Guid) || tipo == typeof(byte[]))
                {
                    object valor = propiedad.GetValue(envio);
                    columnas[propiedad.Name] = valor is string texto ? texto.TrimEnd() : valor;
                }
            }
            return columnas;
        }

        public static string Json(EnviosAgencia envio, IEnumerable<EnvioHistoria> historia, string usuario, DateTime momento)
            => JsonConvert.SerializeObject(new
            {
                Envio = Columnas(envio),
                Historia = (historia ?? Enumerable.Empty<EnvioHistoria>()).Select(h => new
                {
                    h.Numero,
                    h.Campo,
                    h.ValorAnterior,
                    h.Observaciones,
                    h.Usuario,
                    h.FechaModificacion
                }).ToList(),
                BorradoPor = usuario,
                BorradoEl = momento
            });

        public static string Recortar(string valor, int maximo)
            => valor == null ? string.Empty : valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }
}
