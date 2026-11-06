using NestoAPI.Infraestructure.Notificaciones;
using NestoAPI.Infraestructure.Verifactu;
using NestoAPI.Models;
using NestoAPI.Models.PreparacionAlmacen;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
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
        /// <summary>
        /// Sin él (o 0), una. NestoAPI#839: varias de una vez, como mucho las que faltan de la línea (lo enviado menos lo leído en
        /// curso), o una si ya no falta ninguna (se puede apuntar una de más, como antes). Un solo aviso con la cantidad.
        /// </summary>
        public int Unidades { get; set; }
        public string Dispositivo { get; set; }
        /// <summary>
        /// Revisión 30/10 (opcional): una clave por unidad apuntada (un GUID de la app). Si llega una que ya se procesó (la PDA
        /// repite tras un timeout), se devuelve la respuesta de la primera vez sin volver a avisar. Sin ella, se avisa siempre.
        /// </summary>
        public string ClaveIdempotencia { get; set; }
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

        public const int LONGITUD_MAXIMA_CLAVE = 100;

        /// <summary>NestoAPI#797: lo que dice la respuesta de una muestra (MMP) sin código, que no avisa a nadie.</summary>
        public const string TEXTO_MUESTRA_SIN_CODIGO = "muestra sin código de barras: no se avisa a nadie";

        private readonly IServicioRecepcionReposiciones reposiciones;
        private readonly IAvisadorSinEtiqueta avisador;
        private readonly IRepositorioIdempotenciaSinEtiqueta idempotencia;
        private readonly Productos.IRepositorioCodigosBarras codigos;
        private readonly IRepositorioLecturasRecepcion lecturas;

        /// <param name="idempotencia">Dónde se apuntan las claves ya procesadas. Sin él, la clave se ignora (se avisa siempre).</param>
        /// <param name="codigos">Ariadna#92: los códigos activos del producto (ProductosCodigosBarras). Sin él, solo el de la ficha.</param>
        /// <param name="lecturas">NestoAPI#839: lo leído en curso (RecepcionesLecturasEnCurso), para el tope de unidades. Sin él (o
        /// sin la tabla), lo que falta es todo lo enviado.</param>
        public ServicioSinEtiquetaRecepcion(IServicioRecepcionReposiciones reposiciones, IAvisadorSinEtiqueta avisador,
            IRepositorioIdempotenciaSinEtiqueta idempotencia = null, Productos.IRepositorioCodigosBarras codigos = null,
            IRepositorioLecturasRecepcion lecturas = null)
        {
            this.reposiciones = reposiciones ?? throw new ArgumentNullException(nameof(reposiciones));
            this.avisador = avisador ?? throw new ArgumentNullException(nameof(avisador));
            this.idempotencia = idempotencia;
            this.codigos = codigos;
            this.lecturas = lecturas;
        }

        /// <summary>
        /// NestoAPI#839: cuántas se pueden apuntar de una vez: las que faltan (enviado − leído) o, si no falta ninguna, una (la
        /// unidad de más que ya se admitía).
        /// </summary>
        public static int TopeUnidades(int enviado, int leido) => Math.Max(enviado - leido, 1);

        /// <summary>Lo leído en curso del producto (la app lo anota al contar), o 0 si no se sabe.</summary>
        private async Task<int> LeerLeido(string empresa, string almacen, int traspaso, string producto, string usuario)
        {
            if (lecturas == null)
            {
                return 0;
            }
            try
            {
                if (!lecturas.TablaExiste())
                {
                    return 0;
                }
                var clave = new ClaveRecepcionEnCurso
                {
                    Empresa = string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim(),
                    Tipo = OrigenRecepcionReposiciones.TIPO.Trim().ToUpperInvariant(),
                    Documento = traspaso.ToString(CultureInfo.InvariantCulture),
                    Almacen = string.IsNullOrWhiteSpace(almacen) ? Constantes.Almacenes.ALGETE : almacen.Trim().ToUpperInvariant()
                };
                List<FilaLecturaRecepcionEnCurso> filas = await lecturas.Abrir(clave, usuario, null).ConfigureAwait(false)
                    ?? new List<FilaLecturaRecepcionEnCurso>();
                return filas
                    .Where(f => string.Equals(f.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                    .Sum(f => f.Cantidad);
            }
            catch (Exception ex)
            {
                // Sin lo leído, el tope es lo enviado (no se impide apuntar)
                ElmahHelper.Log(new Exception($"[Sin etiqueta 839] No se ha podido leer lo leído de la reposición {traspaso} ({producto}): {ex.Message}", ex));
                return 0;
            }
        }

        /// <summary>
        /// Ariadna#92: el código de barras que el producto trae en el envase (el de la ficha o uno activo de ProductosCodigosBarras;
        /// los internos del 29 no cuentan, porque esos van en la etiqueta que pone el origen), o null si no tiene.
        /// </summary>
        public static string CodigoDelEnvase(string codigoFicha, IEnumerable<string> activos)
        {
            return Productos.CompletadorCodigosBarras.Juntar(codigoFicha, activos)
                .FirstOrDefault(c => !Productos.CodigoBarrasInterno.EsInterno(c));
        }

        private async Task<string> LeerCodigoDelEnvase(string empresa, LineaReposicionDTO linea)
        {
            List<string> activos = linea.CodigosBarras;
            if ((activos == null || activos.Count == 0) && codigos != null)
            {
                try
                {
                    Dictionary<string, List<string>> leidos = await codigos.CodigosActivos(empresa, new List<string> { linea.Producto.Trim() }).ConfigureAwait(false);
                    if (leidos != null && leidos.TryGetValue(linea.Producto.Trim(), out List<string> suyos))
                    {
                        activos = suyos;
                    }
                }
                catch (Exception ex)
                {
                    // Sin los códigos activos, se decide con el de la ficha
                    ElmahHelper.Log(new Exception($"[Sin etiqueta 92] No se han podido leer los códigos del {linea.Producto}: {ex.Message}", ex));
                }
            }
            return CodigoDelEnvase(linea.CodigoBarras, activos);
        }

        /// <summary>La clave tal como se apunta: con el documento, para que la misma clave en otra reposición no choque.</summary>
        internal static string ClaveCompleta(string empresa, string almacen, int traspaso, string clave)
            => string.Join("|", empresa?.Trim(), almacen?.Trim().ToUpperInvariant(), OrigenRecepcionReposiciones.TIPO,
                traspaso.ToString(CultureInfo.InvariantCulture), clave.Trim());

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

        public Task<ResultadoSinEtiquetaRecepcionDTO> Apuntar(string tipo, string empresa, string almacen, string documento,
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
            string clave = peticion?.ClaveIdempotencia?.Trim();
            if (string.IsNullOrEmpty(clave) || idempotencia == null)
            {
                return ApuntarYAvisar(empresa, almacen, traspaso, peticion, usuario);
            }
            if (clave.Length > LONGITUD_MAXIMA_CLAVE)
            {
                throw new ArgumentException($"La clave de idempotencia no puede pasar de {LONGITUD_MAXIMA_CLAVE} caracteres.");
            }
            return idempotencia.UnaVez(ClaveCompleta(empresa, almacen, traspaso, clave),
                () => ApuntarYAvisar(empresa, almacen, traspaso, peticion, usuario));
        }

        private async Task<ResultadoSinEtiquetaRecepcionDTO> ApuntarYAvisar(string empresa, string almacen, int traspaso,
            SinEtiquetaRecepcionDTO peticion, string usuario)
        {
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
            int enviado = recepcion.Lineas
                .Where(l => string.Equals(l.Producto?.Trim(), producto, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.Cantidad);
            int leido = unidades > 1 ? await LeerLeido(empresa, almacen, traspaso, producto, usuario).ConfigureAwait(false) : 0;
            if (unidades > TopeUnidades(enviado, leido))
            {
                // NestoAPI#839 (Carlos 06/11/26): como mucho lo que falta de la línea; no se cuenta ni se avisa nada
                int faltan = enviado - leido;
                string cuantas = faltan <= 0 ? "ya no falta ninguna (solo se puede apuntar una de más)"
                    : faltan == 1 ? "solo falta 1" : $"solo faltan {faltan}";
                throw new ArgumentException($"No se pueden apuntar {unidades} unidades sin etiqueta del {producto}: {cuantas}.");
            }
            string ud = Unidades(unidades);
            if (linea.SinCodigoAdmitido)
            {
                // NestoAPI#797: una muestra (MMP) sin código no se etiqueta: se cuenta y no se avisa ni al origen ni a Compras
                return new ResultadoSinEtiquetaRecepcionDTO
                {
                    Mensaje = $"{Apuntadas(unidades)} {ud} sin etiqueta del {producto}: {TEXTO_MUESTRA_SIN_CODIGO}.",
                    Avisados = new List<string>()
                };
            }
            string codigoEnvase = await LeerCodigoDelEnvase(empresa, linea).ConfigureAwait(false);
            if (codigoEnvase != null && motivo == MOTIVO_NO_LLEVA)
            {
                // Ariadna#92 (Carlos 04/11/26): con código no hace falta etiqueta: se lee el del envase
                throw new ArgumentException($"El {producto} tiene código de barras {codigoEnvase}: léalo del envase; «no lleva etiqueta» es solo para los productos sin código.");
            }
            ReposicionPendienteDTO pendiente = (await reposiciones.LeerPendientes(empresa, almacen).ConfigureAwait(false) ?? new List<ReposicionPendienteDTO>())
                .FirstOrDefault(r => r.Traspaso == traspaso);
            string origen = pendiente?.Origen?.Trim();
            string nombreOrigen = string.IsNullOrWhiteSpace(pendiente?.NombreOrigen) ? origen : pendiente.NombreOrigen.Trim();
            string quien = string.IsNullOrWhiteSpace(usuario) ? "Alguien" : usuario.Trim().Substring(usuario.Trim().LastIndexOf('\\') + 1);
            string nombreProducto = string.IsNullOrWhiteSpace(linea.Descripcion) ? producto : $"{producto} {linea.Descripcion.Trim()}";
            if (codigoEnvase != null)
            {
                return await AvisarACompras(traspaso, origen, almacen, producto, nombreProducto, codigoEnvase, quien, unidades, motivo, comentario,
                    peticion.Dispositivo).ConfigureAwait(false);
            }
            string titulo = unidades == 1
                ? $"Reposición {traspaso} ({origen ?? "¿?"} → {almacen}): llega sin etiqueta el {producto}"
                : $"Reposición {traspaso} ({origen ?? "¿?"} → {almacen}): llegan {ud} sin etiqueta del {producto}";
            var lineas = new List<string>
            {
                $"{quien} ha recibido {ud} sin etiqueta del {nombreProducto} sin poder {Escanearla(unidades)}: {TextoMotivo(motivo, comentario)}.",
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

            return await Resultado(traspaso, producto, unidades,
                () => avisador.Avisar(empresa, origen, nombreOrigen, pendiente?.Usuario, titulo, lineas)).ConfigureAwait(false);
        }

        /// <summary>
        /// Ariadna#92: el producto tiene código y el del envase no se lee o no coincide: no es cosa del origen (no tenía que
        /// etiquetarlo), sino de Compras, que tiene que dar de alta o cambiar el código.
        /// </summary>
        private Task<ResultadoSinEtiquetaRecepcionDTO> AvisarACompras(int traspaso, string origen, string almacen, string producto,
            string nombreProducto, string codigoEnvase, string quien, int unidades, string motivo, string comentario, string dispositivo)
        {
            string titulo = unidades == 1
                ? $"Reposición {traspaso} ({origen ?? "¿?"} → {almacen}): el código del {producto} no se lee o no coincide"
                : $"Reposición {traspaso} ({origen ?? "¿?"} → {almacen}): {Unidades(unidades)} del {producto}: el código no se lee o no coincide";
            string queLePasa = motivo == MOTIVO_NO_SE_LEE ? "el código no se lee" : TextoMotivo(motivo, comentario);
            var lineas = new List<string>
            {
                $"{quien} ha recibido {Unidades(unidades)} sin etiqueta del {nombreProducto} sin poder {Escanearla(unidades)}: {queLePasa}.",
                $"El código de barras de la ficha es {codigoEnvase}.",
                "Compras: compruebe el código del envase y, si es otro, dele de alta o cambie el de la ficha."
            };
            if (!string.IsNullOrWhiteSpace(dispositivo))
            {
                lineas.Add($"Desde {dispositivo.Trim()}.");
            }
            return Resultado(traspaso, producto, unidades, () => avisador.AvisarACompras(titulo, lineas));
        }

        private static async Task<ResultadoSinEtiquetaRecepcionDTO> Resultado(int traspaso, string producto, int unidades, Func<Task<List<string>>> avisar)
        {
            List<string> avisados;
            try
            {
                avisados = await avisar().ConfigureAwait(false) ?? new List<string>();
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
                Mensaje = $"{Apuntadas(unidades)} {Unidades(unidades)} sin etiqueta del {producto}: {aQuien}.",
                Avisados = avisados
            };
        }

        private static string Unidades(int unidades) => unidades == 1 ? "1 unidad" : $"{unidades} unidades";

        private static string Apuntadas(int unidades) => unidades == 1 ? "Apuntada" : "Apuntadas";

        private static string Escanearla(int unidades) => unidades == 1 ? "escanearla" : "escanearlas";
    }

    /// <summary>Revisión 30/10: las claves de «Sin etiqueta» ya procesadas, con la respuesta de la primera vez.</summary>
    public interface IRepositorioIdempotenciaSinEtiqueta
    {
        /// <summary>
        /// Si la clave ya se procesó, su respuesta de entonces (sin llamar a <paramref name="procesar"/>); si no, procesa, apunta la
        /// clave con la respuesta y la devuelve. Si <paramref name="procesar"/> lanza, no se apunta nada.
        /// </summary>
        Task<ResultadoSinEtiquetaRecepcionDTO> UnaVez(string clave, Func<Task<ResultadoSinEtiquetaRecepcionDTO>> procesar);
    }

    /// <summary>
    /// En <c>Modificaciones</c> (Tabla «SinEtiquetaRecepcion», Anterior = la clave, Nuevo = la respuesta en JSON), como la clave del
    /// cobro del reparto (<see cref="Agencias.RepartoPropio.RepositorioCobrosReparto"/>): sin script de BD. Un <c>sp_getapplock</c>
    /// por clave hace que dos reintentos a la vez avisen una sola vez (el segundo espera y devuelve lo del primero).
    /// </summary>
    public class RepositorioIdempotenciaSinEtiqueta : IRepositorioIdempotenciaSinEtiqueta
    {
        internal const string TABLA_MODIFICACIONES = "SinEtiquetaRecepcion";

        public async Task<ResultadoSinEtiquetaRecepcionDTO> UnaVez(string clave, Func<Task<ResultadoSinEtiquetaRecepcionDTO>> procesar)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    var bloqueo = new SqlParameter("@r", SqlDbType.Int) { Direction = ParameterDirection.Output };
                    _ = await db.Database.ExecuteSqlCommandAsync(
                        "EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000",
                        bloqueo, new SqlParameter("@recurso", SqlDbType.NVarChar, 255) { Value = Recurso(clave) }).ConfigureAwait(false);
                    if (bloqueo.Value is int r && r < 0)
                    {
                        throw new InvalidOperationException("Este aviso de «sin etiqueta» ya se está procesando: vuelva a intentarlo en unos segundos.");
                    }
                    string previo = (await db.Modificaciones
                        .Where(m => m.Tabla == TABLA_MODIFICACIONES && m.Anterior == clave)
                        .Select(m => m.Nuevo)
                        .ToListAsync().ConfigureAwait(false)).FirstOrDefault();
                    if (previo != null)
                    {
                        transaccion.RollbackSeguro();
                        return JsonConvert.DeserializeObject<ResultadoSinEtiquetaRecepcionDTO>(previo);
                    }

                    ResultadoSinEtiquetaRecepcionDTO resultado = await procesar().ConfigureAwait(false);
                    _ = db.Modificaciones.Add(new Modificacion
                    {
                        Tabla = TABLA_MODIFICACIONES,
                        Anterior = clave,
                        Nuevo = JsonConvert.SerializeObject(resultado),
                        Fecha = DateTime.Now
                    });
                    _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    transaccion.Commit();
                    return resultado;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        /// <summary>El recurso del applock: como mucho 255 caracteres.</summary>
        internal static string Recurso(string clave)
        {
            string recurso = TABLA_MODIFICACIONES + ":" + clave;
            return recurso.Length <= 255 ? recurso : recurso.Substring(0, 255);
        }
    }

    public interface IAvisadorSinEtiqueta
    {
        /// <returns>A quién se ha avisado, para el usuario («Algete», «Compras»).</returns>
        Task<List<string>> Avisar(string empresa, string origen, string nombreOrigen, string creador, string titulo, IEnumerable<string> lineas);

        /// <summary>Ariadna#92: solo al grupo Compras (el producto tiene código y el del envase no se lee o no coincide).</summary>
        /// <returns>«Compras», o vacía si no hay nadie en el grupo.</returns>
        Task<List<string>> AvisarACompras(string titulo, IEnumerable<string> lineas);
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

            return await Enviar(candidatos, titulo, lineas).ConfigureAwait(false) ? avisados : new List<string>();
        }

        public async Task<List<string>> AvisarACompras(string titulo, IEnumerable<string> lineas)
        {
            List<string> compras = miembrosDelGrupo(Constantes.GruposSeguridad.COMPRAS) ?? new List<string>();
            return await Enviar(compras, titulo, lineas).ConfigureAwait(false) ? new List<string> { "Compras" } : new List<string>();
        }

        /// <returns>false si no había a quién.</returns>
        private async Task<bool> Enviar(IEnumerable<string> candidatos, string titulo, IEnumerable<string> lineas)
        {
            List<string> usuarios = candidatos
                .Select(u => u?.Trim())
                .Where(u => !string.IsNullOrWhiteSpace(u) && !u.StartsWith("(", StringComparison.Ordinal))
                .Select(u => u.Contains("\\") ? u : DOMINIO + u)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!usuarios.Any())
            {
                ElmahHelper.Log(new Exception($"[Sin etiqueta 762] No hay a quién avisar: {titulo}"));
                return false;
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
            return true;
        }
    }
}
