using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>Una fila de EnviosAgenciaCargaBultos: un bulto que un repartidor ha escaneado al cargar la furgoneta.</summary>
    public class CargaBultoReparto
    {
        public int Id { get; set; }
        public int EnvioId { get; set; }
        public int Bulto { get; set; }
        public string Usuario { get; set; }
        public DateTime FechaHora { get; set; }
        public Guid ClaveIdempotencia { get; set; }
        public DateTime FechaRegistro { get; set; }
    }

    /// <summary>Ariadna#63: la tabla de la carga (Scripts/Issue739_CargaFurgoneta.sql), fuera del EDMX.</summary>
    public interface IRepositorioCargaFurgoneta
    {
        /// <summary>¿Existe EnviosAgenciaCargaBultos?</summary>
        bool ScriptAplicado();
        /// <summary>Las cargas de esos envíos, en el orden en que se registraron (Id).</summary>
        Task<List<CargaBultoReparto>> LeerCargas(IReadOnlyCollection<int> envios);
        Task<CargaBultoReparto> LeerPorClave(Guid clave);
        /// <summary>En UNA transacción: la carga y la historia del envío (si la hay).</summary>
        /// <exception cref="ClaveIdempotenciaRepetidaException">Otra petición ya registró esa clave.</exception>
        Task Registrar(CargaBultoReparto carga, IList<EnvioHistoria> historia);
    }

    public class RepositorioCargaFurgoneta : IRepositorioCargaFurgoneta
    {
        public const string TABLA = "EnviosAgenciaCargaBultos";
        private const string COLUMNAS = "Id, EnvioId, CAST(Bulto AS int) AS Bulto, RTRIM(Usuario) AS Usuario, FechaHora, ClaveIdempotencia, FechaRegistro";

        private readonly NVEntities db;

        public RepositorioCargaFurgoneta(NVEntities db)
        {
            this.db = db ?? throw new ArgumentNullException(nameof(db));
        }

        public bool ScriptAplicado() => ColumnasOpcionales.ExisteTabla(db, TABLA);

        public Task<List<CargaBultoReparto>> LeerCargas(IReadOnlyCollection<int> envios)
        {
            if (envios == null || envios.Count == 0)
            {
                return Task.FromResult(new List<CargaBultoReparto>());
            }
            // Enteros en el texto (sin inyección posible): va por IX_EnviosAgenciaCargaBultos_EnvioId.
            string lista = string.Join(", ", envios.Distinct().Select(e => e.ToString(CultureInfo.InvariantCulture)));
            return db.Database.SqlQuery<CargaBultoReparto>($"SELECT {COLUMNAS} FROM dbo.{TABLA} WHERE EnvioId IN ({lista}) ORDER BY Id").ToListAsync();
        }

        public async Task<CargaBultoReparto> LeerPorClave(Guid clave)
            => (await db.Database.SqlQuery<CargaBultoReparto>($"SELECT TOP 1 {COLUMNAS} FROM dbo.{TABLA} WHERE ClaveIdempotencia = @clave",
                new SqlParameter("@clave", SqlDbType.UniqueIdentifier) { Value = clave }).ToListAsync().ConfigureAwait(false)).FirstOrDefault();

        public async Task Registrar(CargaBultoReparto carga, IList<EnvioHistoria> historia)
        {
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    try
                    {
                        _ = await db.Database.ExecuteSqlCommandAsync(
                            $"INSERT INTO dbo.{TABLA} (EnvioId, Bulto, Usuario, FechaHora, ClaveIdempotencia, FechaRegistro) " +
                            "VALUES (@EnvioId, @Bulto, @Usuario, @FechaHora, @Clave, @FechaRegistro)",
                            new SqlParameter("@EnvioId", SqlDbType.Int) { Value = carga.EnvioId },
                            new SqlParameter("@Bulto", SqlDbType.SmallInt) { Value = (short)carga.Bulto },
                            new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(carga.Usuario, 50) },
                            new SqlParameter("@FechaHora", SqlDbType.DateTime) { Value = carga.FechaHora },
                            new SqlParameter("@Clave", SqlDbType.UniqueIdentifier) { Value = carga.ClaveIdempotencia },
                            new SqlParameter("@FechaRegistro", SqlDbType.DateTime) { Value = carga.FechaRegistro }).ConfigureAwait(false);
                    }
                    catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
                    {
                        throw new ClaveIdempotenciaRepetidaException(ex);
                    }
                    if (historia != null && historia.Count > 0)
                    {
                        foreach (EnvioHistoria fila in historia)
                        {
                            _ = db.EnviosHistorias.Add(fila);
                        }
                        _ = await db.SaveChangesAsync().ConfigureAwait(false);
                    }
                    transaccion.Commit();
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        private static string Recortar(string valor, int maximo) => valor == null || valor.Length <= maximo ? valor ?? string.Empty : valor.Substring(0, maximo);
    }

    /// <summary>
    /// Ariadna#63: la carga de la furgoneta. Cada repartidor escanea los bultos que se lleva: el primero que carga un bulto de un
    /// envío se lo queda (EnviosHistoria «En reparto (Nombre)»); si ya lo lleva otro, 409 con details.repartidor, salvo que
    /// confirme Reasignar (queda la traza). El mismo bulto otra vez no es un error. Repartidor = el usuario de la última carga.
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        public const string MENSAJE_FALTA_SCRIPT_CARGA = "Falta lanzar el script Scripts/Issue739_CargaFurgoneta.sql (carga de la furgoneta): " +
            "hasta entonces no se pueden cargar bultos. Avise a Informática.";
        public const string CODIGO_ENVIO_DE_OTRO = "ENVIO_DE_OTRO_REPARTIDOR";
        internal const string CAMPO_HISTORIA_REPARTIDOR = "Repartidor";

        private bool CargaDisponible() => carga != null && carga.ScriptAplicado();

        // ---------------------------------------------------------------- cargar

        public async Task<ResultadoReparto<ResultadoCargaBultoDTO>> Cargar(CargaBultoRepartoDTO peticion, string usuario)
        {
            if (!repositorio.ScriptAplicado() || !CargaDisponible())
            {
                return MalCarga(TipoResultadoReparto.Invalido, !repositorio.ScriptAplicado() ? MENSAJE_FALTA_SCRIPT : MENSAJE_FALTA_SCRIPT_CARGA);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return MalCarga(TipoResultadoReparto.Invalido, "Falta la ClaveIdempotencia de la carga.");
            }
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return MalCarga(TipoResultadoReparto.Invalido, "No se sabe quién carga el bulto.");
            }
            if (!CodigoBultoRepartoPropio.TryLeerBulto(peticion.Codigo, out int numero, out int bulto))
            {
                return MalCarga(TipoResultadoReparto.NoEncontrado,
                    $"El código «{peticion.Codigo?.Trim()}» no es de un bulto del reparto propio (RP, el número del envío, guion y el bulto).");
            }
            EnviosAgencia envio = await repositorio.LeerEnvio(numero).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return MalCarga(TipoResultadoReparto.NoEncontrado, $"No existe el envío {numero} del reparto propio.");
            }
            if (bulto > Math.Max((int)envio.Bultos, 1))
            {
                return MalCarga(TipoResultadoReparto.NoEncontrado,
                    $"El envío {numero} tiene {envio.Bultos} {(envio.Bultos == 1 ? "bulto" : "bultos")}: no existe el bulto {bulto}.");
            }

            // Misma clave = misma respuesta (la cola de Ariadna reintenta), aunque el envío haya cambiado después.
            CargaBultoReparto previa = await carga.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                return await RespuestaClaveRepetida(previa, envio, bulto).ConfigureAwait(false);
            }

            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return MalCarga(TipoResultadoReparto.Conflicto, $"El envío {envio.Numero} ya está entregado: no hay que cargarlo.");
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return MalCarga(TipoResultadoReparto.Conflicto, NoEstaEnReparto(envio));
            }

            List<CargaBultoReparto> cargas = await carga.LeerCargas(new[] { envio.Numero }).ConfigureAwait(false) ?? new List<CargaBultoReparto>();
            string actual = Repartidor(cargas);
            bool esSuyo = actual == null || MismoUsuario(actual, usuario);
            if (!esSuyo && !peticion.Reasignar)
            {
                string nombreActual = NombreRepartidor(actual);
                return new ResultadoReparto<ResultadoCargaBultoDTO>
                {
                    Tipo = TipoResultadoReparto.Conflicto,
                    Mensaje = $"Este envío lo lleva {nombreActual}. Si se lo lleva usted, confírmelo para reasignárselo.",
                    Codigo = CODIGO_ENVIO_DE_OTRO,
                    Detalles = new { repartidor = nombreActual }
                };
            }
            if (esSuyo && actual != null && cargas.Any(c => c.Bulto == bulto && MismoUsuario(c.Usuario, usuario)))
            {
                // El mismo bulto otra vez (otra clave): no es un error y no se apunta dos veces.
                return ResultadoReparto<ResultadoCargaBultoDTO>.Bien(Respuesta(envio, bulto, cargas, usuario));
            }

            DateTime momento = ahora();
            var nueva = new CargaBultoReparto
            {
                EnvioId = envio.Numero,
                Bulto = bulto,
                Usuario = usuario.Trim(),
                FechaHora = peticion.FechaHora ?? momento,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                FechaRegistro = momento
            };
            var historia = new List<EnvioHistoria>();
            if (actual == null || !esSuyo)
            {
                string nombre = NombreRepartidor(usuario);
                historia.Add(new EnvioHistoria
                {
                    NumeroEnvio = envio.Numero,
                    Campo = CAMPO_HISTORIA_REPARTIDOR,
                    ValorAnterior = actual == null ? string.Empty : Recortar(NombreRepartidor(actual), 100),
                    Observaciones = actual == null
                        ? $"En reparto ({nombre})"
                        : $"En reparto ({nombre}): reasignado, antes lo llevaba {NombreRepartidor(actual)}",
                    Usuario = usuario.Trim(),
                    FechaModificacion = momento
                });
            }

            try
            {
                await carga.Registrar(nueva, historia).ConfigureAwait(false);
            }
            catch (ClaveIdempotenciaRepetidaException)
            {
                // Dos peticiones a la vez con la misma clave: la otra ganó. Misma clave = misma respuesta.
                CargaBultoReparto ganadora = await carga.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
                return ganadora != null
                    ? await RespuestaClaveRepetida(ganadora, envio, bulto).ConfigureAwait(false)
                    : MalCarga(TipoResultadoReparto.Conflicto, "Esa carga ya se está registrando. Vuelva a intentarlo.");
            }
            cargas.Add(nueva);
            return ResultadoReparto<ResultadoCargaBultoDTO>.Bien(Respuesta(envio, bulto, cargas, usuario));
        }

        // ---------------------------------------------------------------- sin cargar

        /// <summary>
        /// GET api/RepartoPropio/SinCargar: los envíos del reparto propio Tramitados con fecha <paramref name="fecha"/> (hoy por
        /// defecto) a los que les falta algún bulto por cargar (por quien lo lleva; sin repartidor, todos).
        /// </summary>
        public async Task<ResultadoReparto<List<EnvioSinCargarDTO>>> SinCargar(DateTime? fecha)
        {
            if (!repositorio.ScriptAplicado() || !CargaDisponible())
            {
                return ResultadoReparto<List<EnvioSinCargarDTO>>.Mal(TipoResultadoReparto.Invalido,
                    !repositorio.ScriptAplicado() ? MENSAJE_FALTA_SCRIPT : MENSAJE_FALTA_SCRIPT_CARGA);
            }
            DateTime dia = (fecha ?? ahora()).Date;
            List<EnviosAgencia> envios = (await repositorio.LeerEnviosEnReparto(dia, dia.AddDays(1)).ConfigureAwait(false) ?? new List<EnviosAgencia>())
                .Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO && e.Estado == Constantes.Agencias.ESTADO_TRAMITADO
                    && e.Fecha >= dia && e.Fecha < dia.AddDays(1))
                .ToList();
            List<CargaBultoReparto> cargas = await carga.LeerCargas(envios.Select(e => e.Numero).ToList()).ConfigureAwait(false)
                ?? new List<CargaBultoReparto>();
            return ResultadoReparto<List<EnvioSinCargarDTO>>.Bien(envios
                .Select(e =>
                {
                    List<CargaBultoReparto> suyas = cargas.Where(c => c.EnvioId == e.Numero).ToList();
                    string repartidor = Repartidor(suyas);
                    return new EnvioSinCargarDTO
                    {
                        EnvioId = e.Numero,
                        Pedido = e.Pedido,
                        Nombre = e.Nombre?.Trim(),
                        Poblacion = e.Poblacion?.Trim(),
                        Bultos = Math.Max((int)e.Bultos, 1),
                        BultosCargados = BultosCargados(suyas, repartidor),
                        Repartidor = NombreRepartidor(repartidor)
                    };
                })
                .Where(e => e.BultosCargados < e.Bultos)
                .OrderBy(e => e.Poblacion, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.EnvioId)
                .ToList());
        }

        // ---------------------------------------------------------------- piezas

        /// <summary>Repartidor y bultos cargados de cada envío de la lista (una consulta).</summary>
        private async Task CompletarCarga(List<EnvioRepartoDTO> lista)
        {
            if (lista.Count == 0)
            {
                return;
            }
            List<CargaBultoReparto> cargas = await carga.LeerCargas(lista.Select(e => e.EnvioId).ToList()).ConfigureAwait(false)
                ?? new List<CargaBultoReparto>();
            foreach (EnvioRepartoDTO envio in lista)
            {
                List<CargaBultoReparto> suyas = cargas.Where(c => c.EnvioId == envio.EnvioId).ToList();
                string repartidor = Repartidor(suyas);
                envio.Repartidor = NombreRepartidor(repartidor);
                envio.BultosCargados = BultosCargados(suyas, repartidor);
            }
        }

        private async Task<ResultadoReparto<ResultadoCargaBultoDTO>> RespuestaClaveRepetida(CargaBultoReparto previa, EnviosAgencia envio, int bulto)
        {
            if (previa.EnvioId != envio.Numero || previa.Bulto != bulto)
            {
                return MalCarga(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para cargar el bulto {previa.Bulto} del envío " +
                    $"{previa.EnvioId}. Cada lectura lleva su propia clave.");
            }
            List<CargaBultoReparto> cargas = await carga.LeerCargas(new[] { envio.Numero }).ConfigureAwait(false) ?? new List<CargaBultoReparto>();
            return ResultadoReparto<ResultadoCargaBultoDTO>.Bien(Respuesta(envio, bulto, cargas, previa.Usuario));
        }

        private static ResultadoCargaBultoDTO Respuesta(EnviosAgencia envio, int bulto, IEnumerable<CargaBultoReparto> cargas, string usuario)
            => new ResultadoCargaBultoDTO
            {
                EnvioId = envio.Numero,
                Bulto = bulto,
                Bultos = Math.Max((int)envio.Bultos, 1),
                BultosCargados = BultosCargados(cargas, usuario),
                Repartidor = NombreRepartidor(Repartidor(cargas)),
                Nombre = envio.Nombre?.Trim(),
                Poblacion = envio.Poblacion?.Trim()
            };

        /// <summary>Quién lleva el envío: el usuario de la última carga (con «Reasignar» pasa al nuevo). Null si nadie.</summary>
        internal static string Repartidor(IEnumerable<CargaBultoReparto> cargas)
            => cargas?.OrderBy(c => c.Id).ThenBy(c => c.FechaRegistro).LastOrDefault()?.Usuario?.Trim();

        /// <summary>Bultos distintos cargados por <paramref name="usuario"/> (0 sin usuario).</summary>
        internal static int BultosCargados(IEnumerable<CargaBultoReparto> cargas, string usuario)
            => usuario == null ? 0 : (cargas ?? Enumerable.Empty<CargaBultoReparto>()).Where(c => MismoUsuario(c.Usuario, usuario)).Select(c => c.Bulto).Distinct().Count();

        /// <summary>Sin dominio y sin distinguir mayúsculas: «NUEVAVISION\Andre» es «andre».</summary>
        internal static bool MismoUsuario(string uno, string otro)
        {
            string a = NombreRepartidor(uno);
            string b = NombreRepartidor(otro);
            return a != null && b != null && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>«Andre» a partir de «NUEVAVISION\Andre». Null si viene vacío.</summary>
        internal static string NombreRepartidor(string usuario)
        {
            string limpio = usuario?.Trim();
            if (string.IsNullOrEmpty(limpio))
            {
                return null;
            }
            int barra = limpio.LastIndexOf('\\');
            return barra >= 0 && barra < limpio.Length - 1 ? limpio.Substring(barra + 1) : limpio;
        }

        private static ResultadoReparto<ResultadoCargaBultoDTO> MalCarga(TipoResultadoReparto tipo, string mensaje)
            => ResultadoReparto<ResultadoCargaBultoDTO>.Mal(tipo, mensaje);
    }
}
