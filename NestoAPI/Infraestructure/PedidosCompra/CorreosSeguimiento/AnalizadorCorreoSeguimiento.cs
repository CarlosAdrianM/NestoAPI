using System;
using System.Collections.Generic;
using System.Linq;

namespace NestoAPI.Infraestructure.PedidosCompra.CorreosSeguimiento
{
    /// <summary>NestoAPI#857: una dirección o un dominio marcado por Compras como de una agencia (tabla AgenciasEntradaRemitentes).</summary>
    public class RemitenteMarcado
    {
        public int Id { get; set; }
        public string Agencia { get; set; }
        /// <summary>Dirección completa (contiene «@») o dominio («ejemplo-transportes.es»), en minúsculas.</summary>
        public string Remitente { get; set; }
        public bool Activo { get; set; }
        public string Usuario { get; set; }
        public DateTime? FechaModificacion { get; set; }

        /// <summary>¿Casa con esa dirección? Una dirección, exacta; un dominio, también sus subdominios.</summary>
        public bool Casa(string direccion)
        {
            if (string.IsNullOrWhiteSpace(direccion) || string.IsNullOrWhiteSpace(Remitente))
            {
                return false;
            }
            string marcado = Remitente.Trim().ToLowerInvariant();
            return marcado.Contains("@")
                ? string.Equals(direccion.Trim(), marcado, StringComparison.OrdinalIgnoreCase)
                : TextoCorreo.HostDe(TextoCorreo.Dominio(direccion), marcado);
        }
    }

    /// <summary>NestoAPI#857: lo que se ha sacado de un correo.</summary>
    public class AnalisisCorreoSeguimiento
    {
        /// <summary>Solo se guardan los que traen algún seguimiento o vienen de un remitente marcado.</summary>
        public bool Guardar { get; set; }
        /// <summary>La agencia reconocida (o la del remitente marcado); null si solo lo ha captado el genérico y el texto no la nombra tras una etiqueta.</summary>
        public string Agencia { get; set; }
        public int? HorasHastaEntrega { get; set; }
        /// <summary>
        /// La agencia se ha sabido por su nombre tras una etiqueta del texto («Transportista: …»), no por un enlace, el remitente o
        /// un código suyo (los enlaces y códigos los ha sacado el genérico). No se guarda: la tabla no tiene columna para ello.
        /// </summary>
        public bool AgenciaPorNombreEnTexto { get; set; }
        public List<SeguimientoCorreo> Seguimientos { get; set; } = new List<SeguimientoCorreo>();
        public RemitenteMarcado RemitenteMarcado { get; set; }
        /// <summary>Como mucho <see cref="AnalizadorCorreoSeguimiento.LONGITUD_EXTRACTO"/> caracteres alrededor del seguimiento.</summary>
        public string Extracto { get; set; }
        /// <summary>Remitentes originales de un reenvío (líneas «De:» del cuerpo).</summary>
        public List<string> RemitentesOriginales { get; set; } = new List<string>();
    }

    /// <summary>
    /// NestoAPI#857: clasificación PURA de un correo del buzón de Compras. Orden: las agencias conocidas (la primera que saca un
    /// seguimiento gana); si ninguna, el remitente marcado (con su agencia) y lo que capte el genérico; si tampoco, el genérico solo,
    /// con la agencia que el texto nombre tras una etiqueta (<see cref="AgenciaPorNombreEnTexto"/>) o null. Mira el remitente directo y, en un reenvío, los originales.
    /// </summary>
    public static class AnalizadorCorreoSeguimiento
    {
        public const int LONGITUD_EXTRACTO = 500;

        public static AnalisisCorreoSeguimiento Analizar(CorreoCompras correo, IEnumerable<IAgenciaEntrada> agencias,
            IEnumerable<RemitenteMarcado> marcados, IAgenciaEntrada generica = null)
        {
            var analisis = new AnalisisCorreoSeguimiento();
            if (correo == null)
            {
                return analisis;
            }
            generica = generica ?? AgenciasEntradaRegistradas.Generica;
            analisis.RemitentesOriginales = TextoCorreo.RemitentesOriginales(correo.Texto);
            var direcciones = new List<string>();
            if (!string.IsNullOrWhiteSpace(correo.RemitenteDireccion))
            {
                direcciones.Add(correo.RemitenteDireccion.Trim().ToLowerInvariant());
            }
            direcciones.AddRange(analisis.RemitentesOriginales);
            analisis.RemitenteMarcado = (marcados ?? Enumerable.Empty<RemitenteMarcado>())
                .Where(m => m != null && m.Activo)
                .FirstOrDefault(m => direcciones.Any(m.Casa));

            foreach (IAgenciaEntrada agencia in agencias ?? Enumerable.Empty<IAgenciaEntrada>())
            {
                List<SeguimientoCorreo> seguimientos = agencia.ExtraerSeguimientos(correo) ?? new List<SeguimientoCorreo>();
                if (seguimientos.Any())
                {
                    analisis.Agencia = agencia.Nombre;
                    analisis.HorasHastaEntrega = agencia.HorasHastaEntrega;
                    analisis.Seguimientos = seguimientos;
                    break;
                }
            }
            if (!analisis.Seguimientos.Any())
            {
                analisis.Seguimientos = generica.ExtraerSeguimientos(correo) ?? new List<SeguimientoCorreo>();
                if (analisis.RemitenteMarcado != null)
                {
                    analisis.Agencia = analisis.RemitenteMarcado.Agencia?.Trim();
                    analisis.HorasHastaEntrega = AgenciasEntradaRegistradas.PorNombre(analisis.Agencia)?.HorasHastaEntrega;
                }
                if (string.IsNullOrWhiteSpace(analisis.Agencia) && analisis.Seguimientos.Any())
                {
                    // Correo de un proveedor que nombra la agencia tras una etiqueta («Transportista: SCHENKER STANDARD»)
                    IAgenciaEntrada porNombre = AgenciaPorNombreEnTexto.Buscar(correo, agencias);
                    if (porNombre != null)
                    {
                        analisis.Agencia = porNombre.Nombre;
                        analisis.HorasHastaEntrega = porNombre.HorasHastaEntrega;
                        analisis.AgenciaPorNombreEnTexto = true;
                    }
                }
            }
            analisis.Guardar = analisis.Seguimientos.Any() || analisis.RemitenteMarcado != null;
            if (analisis.Guardar)
            {
                analisis.Extracto = Extracto(correo, analisis.Seguimientos.FirstOrDefault());
            }
            return analisis;
        }

        /// <summary>
        /// El texto alrededor del seguimiento: centrado en el código si aparece en el texto; si no, en la primera palabra de
        /// seguimiento; si tampoco, el principio. Nunca el cuerpo entero.
        /// </summary>
        public static string Extracto(CorreoCompras correo, SeguimientoCorreo seguimiento, int longitud = LONGITUD_EXTRACTO)
        {
            string texto = correo?.Texto ?? string.Empty;
            if (texto.Length <= longitud)
            {
                return texto;
            }
            int centro = -1;
            if (!string.IsNullOrEmpty(seguimiento?.Codigo))
            {
                centro = texto.IndexOf(seguimiento.Codigo, StringComparison.OrdinalIgnoreCase);
            }
            if (centro < 0)
            {
                string minusculas = texto.ToLowerInvariant();
                centro = AgenciaEntradaPorPatrones.PalabrasSeguimiento
                    .Select(p => minusculas.IndexOf(p, StringComparison.Ordinal))
                    .Where(i => i >= 0)
                    .DefaultIfEmpty(0)
                    .Min();
            }
            int inicio = Math.Max(0, Math.Min(centro - longitud / 2, texto.Length - longitud));
            return texto.Substring(inicio, longitud).Trim();
        }
    }
}
