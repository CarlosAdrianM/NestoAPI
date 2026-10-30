using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PreparacionAlmacen
{
    /// <summary>NestoAPI#762: lo que manda la app al contar una unidad que ha llegado sin etiqueta.</summary>
    public class SinEtiquetaRecepcionDTO
    {
        public string Producto { get; set; }
        /// <summary>NoLleva | NoSeLee | Otro (este con <see cref="Comentario"/>).</summary>
        public string Motivo { get; set; }
        public string Comentario { get; set; }
        /// <summary>Sin él (o 0), una. Se cuenta unidad a unidad: las apps mandan 1.</summary>
        public int Unidades { get; set; }
        public string Dispositivo { get; set; }
    }

    public class ResultadoSinEtiquetaRecepcionDTO
    {
        /// <summary>Para enseñarlo tal cual («Apuntada 1 unidad sin etiqueta del 17404: se ha avisado a Algete y a Compras.»).</summary>
        public string Mensaje { get; set; }
        /// <summary>A quién se ha avisado, para enseñarlo («Algete», «Compras»).</summary>
        public List<string> Avisados { get; set; } = new List<string>();
    }

    public interface IServicioSinEtiquetaRecepcion
    {
        /// <exception cref="ArgumentException">Tipo que no es REPO, motivo no válido, producto que no viene… El motivo es para el usuario.</exception>
        /// <exception cref="KeyNotFoundException">No hay ninguna reposición pendiente con ese número en ese almacén.</exception>
        Task<ResultadoSinEtiquetaRecepcionDTO> Apuntar(string tipo, string empresa, string almacen, string documento, SinEtiquetaRecepcionDTO peticion, string usuario);
    }

    /// <summary>
    /// NestoAPI#762 (sugerencia 909, Carlos 30/10/26): una reposición se recibe SOLO escaneando; lo que llega sin etiqueta se
    /// cuenta unidad a unidad en la app con su motivo y cada caso avisa al ORIGEN (que es quien tenía que etiquetarlo) y a
    /// Compras. Aquí solo se avisa: la unidad la cuenta la app con sus lecturas (llega al terminar como cualquier otra).
    /// </summary>
    public class ServicioSinEtiquetaRecepcion : IServicioSinEtiquetaRecepcion
    {
        public const string MOTIVO_NO_LLEVA = "NoLleva";
        public const string MOTIVO_NO_SE_LEE = "NoSeLee";
        public const string MOTIVO_OTRO = "Otro";
        public const int MAXIMO_UNIDADES = 50;

        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IAvisadorSinEtiqueta avisador;

        public ServicioSinEtiquetaRecepcion(IServicioRecepcionReposiciones reposiciones, IAvisadorSinEtiqueta avisador)
        {
            this.reposiciones = reposiciones ?? throw new ArgumentNullException(nameof(reposiciones));
            this.avisador = avisador ?? throw new ArgumentNullException(nameof(avisador));
        }

        /// <summary>El motivo normalizado (NoLleva, NoSeLee, Otro; admite mayúsculas y los textos), o null si no es ninguno.</summary>
        public static string NormalizarMotivo(string motivo)
        {
            string limpio = (motivo ?? string.Empty).Replace(" ", string.Empty).Trim();
            foreach (string valido in new[] { MOTIVO_NO_LLEVA, MOTIVO_NO_SE_LEE, MOTIVO_OTRO })
            {
                if (string.Equals(limpio, valido, StringComparison.OrdinalIgnoreCase))
                {
                    return valido;
                }
            }
            return null;
        }

        /// <summary>El motivo como se lee en el aviso.</summary>
        public static string TextoMotivo(string motivo, string comentario)
        {
            switch (motivo)
            {
                case MOTIVO_NO_LLEVA:
                    return "no lleva etiqueta";
                case MOTIVO_NO_SE_LEE:
                    return "la etiqueta no se lee";
                default:
                    return "otro motivo: " + (comentario ?? string.Empty).Trim();
            }
        }

        public async Task<ResultadoSinEtiquetaRecepcionDTO> Apuntar(string tipo, string empresa, string almacen, string documento,
            SinEtiquetaRecepcionDTO peticion, string usuario)
        {
            if (!string.Equals(tipo?.Trim(), OrigenRecepcionReposiciones.TIPO, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("«Sin etiqueta» solo se usa al recibir reposiciones.");
            }
            if (!int.TryParse(documento?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int traspaso))
            {
                throw new ArgumentException($"«{documento}» no es un número de traspaso.");
            }
            string producto = peticion?.Producto?.Trim();
            if (string.IsNullOrEmpty(producto))
            {
                throw new ArgumentException("Falta el producto que ha llegado sin etiqueta.");
            }
            string motivo = NormalizarMotivo(peticion.Motivo);
            if (motivo == null)
            {
                throw new ArgumentException("El motivo tiene que ser «No lleva etiqueta», «La etiqueta no se lee» u «Otro».");
            }
            string comentario = string.IsNullOrWhiteSpace(peticion.Comentario) ? null : peticion.Comentario.Trim();
            if (motivo == MOTIVO_OTRO && comentario == null)
            {
                throw new ArgumentException("Con «Otro», escriba el motivo.");
            }
            int unidades = peticion.Unidades <= 0 ? 1 : peticion.Unidades;
            if (unidades > MAXIMO_UNIDADES)
            {
                throw new ArgumentException($"Como mucho se apuntan {MAXIMO_UNIDADES} unidades sin etiqueta de una vez.");
            }

            RecepcionReposicionDTO recepcion = await reposiciones.LeerRecepcion(empresa, almacen, traspaso).ConfigureAwait(false)
                ?? throw new KeyNotFoundException($"No hay ninguna reposición {traspaso} pendiente de recibir en {almacen}.");
            LineaReposicionDTO linea = (recepcion.Lineas ?? new List<LineaReposicionDTO>())
                .FirstOrDefault(l => string.Equals(l.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase));
            if (linea == null)
            {
                throw new ArgumentException($"El {producto} no viene en la reposición {traspaso}: «Sin etiqueta» es para lo que viene en ella.");
            }
            ReposicionPendienteDTO pendiente = (await reposiciones.LeerPendientes(empresa, almacen).ConfigureAwait(false) ?? new List<ReposicionPendienteDTO>())
                .FirstOrDefault(r => r.Traspaso == traspaso);
            string origen = pendiente?.Origen?.Trim();
            string nombreOrigen = string.IsNullOrWhiteSpace(pendiente?.NombreOrigen) ? origen : pendiente.NombreOrigen.Trim();
            string quien = string.IsNullOrWhiteSpace(usuario) ? "Alguien" : usuario.Trim().Substring(usuario.Trim().LastIndexOf('\\') + 1);
            string nombreProducto = string.IsNullOrWhiteSpace(linea.Descripcion) ? producto : $"{producto} {linea.Descripcion.Trim()}";
            string ud = unidades == 1 ? "1 unidad" : $"{unidades} unidades";
            string titulo = $"Reposición {traspaso} ({origen ?? "¿?"} → {almacen}): llega sin etiqueta el {producto}";
            var lineas = new List<string>
            {
                $"{quien} ha recibido {ud} del {nombreProducto} sin poder escanearla: {TextoMotivo(motivo, comentario)}.",
                "El almacén de origen tiene que etiquetar lo que no lleve código de barras antes de enviarlo (etiqueta EAN-13 desde la reposición)."
            };
            if (motivo == MOTIVO_NO_SE_LEE)
            {
                lineas.Add("Compras: compruebe el código de barras del producto (el del envase o la etiqueta no se lee).");
            }
            else
            {
                lineas.Add("Compras: si el producto no tiene código de barras, al imprimir su etiqueta se le da uno interno (empieza por 29).");
            }
            if (!string.IsNullOrWhiteSpace(peticion.Dispositivo))
            {
                lineas.Add($"Desde {peticion.Dispositivo.Trim()}.");
            }

            List<string> avisados;
            try
            {
                avisados = await avisador.Avisar(empresa, origen, nombreOrigen, pendiente?.Usuario, titulo, lineas).ConfigureAwait(false) ?? new List<string>();
            }
            catch (Exception ex)
            {
                // Avisar nunca impide contar la unidad
                ElmahHelper.Log(new Exception($"[Sin etiqueta 762] No se ha podido avisar de la reposición {traspaso} ({producto}): {ex.Message}", ex));
                avisados = new List<string>();
            }
            string aQuien = avisados.Count == 0 ? "no se ha podido avisar a nadie"
                : "se ha avisado a " + (avisados.Count == 1 ? avisados[0] : string.Join(", ", avisados.Take(avisados.Count - 1)) + " y a " + avisados.Last());
            return new ResultadoSinEtiquetaRecepcionDTO
            {
                Mensaje = $"Apuntada {ud} sin etiqueta del {producto}: {aQuien}.",
                Avisados = avisados
            };
        }
    }

    public interface IAvisadorSinEtiqueta
    {
        /// <returns>A quién se ha avisado, para el usuario («Algete», «Compras»).</returns>
        Task<List<string>> Avisar(string empresa, string origen, string nombreOrigen, string creador, string titulo, IEnumerable<string> lineas);
    }

    /// <summary>
    /// NestoAPI#762: el aviso de lo que llega sin etiqueta, al buzón de Nesto y al de Ariadna (como los de las diferencias de
    /// reposición, <see cref="AvisadorReposiciones"/>): a quien preparó la reposición, al ORIGEN (el grupo Almacén si es
    /// Algete; si es una tienda, su gente) y al grupo Compras.
    /// </summary>
    public class AvisadorSinEtiqueta : IAvisadorSinEtiqueta
    {
        public const string TIPO_NOTIFICACION = "SinEtiquetaReposicion";
        private const string DOMINIO = "NUEVAVISION\\";

        private readonly IServicioNotificacionesPush notificaciones;
        private readonly Func<string, List<string>> miembrosDelGrupo;
        private readonly Func<string, string, Task<List<string>>> usuariosDeLaTienda;

        public AvisadorSinEtiqueta(IServicioNotificacionesPush notificaciones) : this(notificaciones, null, null)
        {
        }

        internal AvisadorSinEtiqueta(IServicioNotificacionesPush notificaciones, Func<string, List<string>> miembrosDelGrupo,
            Func<string, string, Task<List<string>>> usuariosDeLaTienda)
        {
            this.notificaciones = notificaciones ?? throw new ArgumentNullException(nameof(notificaciones));
            this.miembrosDelGrupo = miembrosDelGrupo ?? MiembrosGrupoDominio.Leer;
            this.usuariosDeLaTienda = usuariosDeLaTienda ?? LeerUsuariosDeLaTienda;
        }

        private static async Task<List<string>> LeerUsuariosDeLaTienda(string empresa, string almacen)
        {
            using (var db = new NVEntities())
            {
                return await Reposiciones.AvisadorReposicionAutomatica.UsuariosDelAlmacen(db, empresa, almacen).ConfigureAwait(false);
            }
        }

        public async Task<List<string>> Avisar(string empresa, string origen, string nombreOrigen, string creador, string titulo, IEnumerable<string> lineas)
        {
            var avisados = new List<string>();
            var candidatos = new List<string>();
            // La de las reposiciones automáticas no la prepara nadie: «ReposicionAutomatica» no tiene buzón
            string sinDominio = creador?.Trim().Substring(creador.Trim().LastIndexOf('\\') + 1);
            if (!string.IsNullOrWhiteSpace(sinDominio) && !string.Equals(sinDominio, Reposiciones.ServicioReposicionAutomatica.USUARIO_AUTOMATICO, StringComparison.OrdinalIgnoreCase))
            {
                candidatos.Add(creador.Trim());
            }
            string almacenOrigen = origen?.Trim().ToUpperInvariant();
            List<string> delOrigen = string.IsNullOrEmpty(almacenOrigen) || almacenOrigen == Constantes.Almacenes.ALGETE
                ? miembrosDelGrupo(Constantes.GruposSeguridad.ALMACEN) ?? new List<string>()
                : await usuariosDeLaTienda(empresa?.Trim(), almacenOrigen).ConfigureAwait(false) ?? new List<string>();
            candidatos.AddRange(delOrigen);
            if (candidatos.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                avisados.Add(string.IsNullOrWhiteSpace(nombreOrigen) ? (almacenOrigen ?? "el origen") : nombreOrigen.Trim());
            }
            List<string> compras = miembrosDelGrupo(Constantes.GruposSeguridad.COMPRAS) ?? new List<string>();
            if (compras.Any(c => !string.IsNullOrWhiteSpace(c)))
            {
                avisados.Add("Compras");
            }
            candidatos.AddRange(compras);

            List<string> usuarios = candidatos
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Sin etiqueta 762] No hay a quién avisar: {titulo}"));
                return new List<string>();
            }
            var notificacion = new NotificacionPushDTO
            {
                Titulo = titulo,
                Cuerpo = string.Join(Environment.NewLine, (lineas ?? Enumerable.Empty<string>()).Where(l => !string.IsNullOrWhiteSpace(l))),
                Tipo = TIPO_NOTIFICACION,
                Datos = new Dictionary<string, string> { ["tipo"] = TIPO_NOTIFICACION }
            };
            foreach (string usuario in usuarios)
            {
                await notificaciones.GuardarEnBuzonDeUsuario(usuario, Constantes.Aplicaciones.NESTO, notificacion).ConfigureAwait(false);
                await notificaciones.GuardarEnBuzonDeUsuario(usuario.Substring(usuario.LastIndexOf('\\') + 1), Constantes.Aplicaciones.ARIADNA, notificacion)
                    .ConfigureAwait(false);
            }
            return avisados;
        }
    }
}
