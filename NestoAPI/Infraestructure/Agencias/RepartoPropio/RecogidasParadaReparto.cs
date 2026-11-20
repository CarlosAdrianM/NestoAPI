using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.SqlClient;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Punto 5 (Carlos 19/11/26): la parada de una NOTA DE RECOGIDA. LAS REGLAS, puras:
    /// <list type="bullet">
    /// <item>«No se ha recogido» (motivo obligatorio): la parada sale del reparto como Cerrado a mano (5) con
    /// «No recogido (reparto propio): motivo» y no se crean bultos NR. Si ya hay bultos NR en una furgoneta o está abonada, no.</item>
    /// <item>El comentario al recogerla («recogido, pero sin el manípulo»): historia, DetalleEstado «Recogido (reparto propio): …» y prueba.</item>
    /// <item>«Anular recogida» de una nota YA en reparto: misma salida (5, «Recogida anulada: motivo»), con rastro en Modificaciones; nunca
    /// si sus bultos NR ya están en una furgoneta o la recogida está abonada.</item>
    /// </list>
    /// </summary>
    public static class ReglasParadaRecogida
    {
        public const int MAXIMO_TEXTO = 500;
        /// <summary>DetalleEstado es varchar(100).</summary>
        public const int LONGITUD_DETALLE = 100;
        public const string PREFIJO_RECOGIDO = "Recogido (reparto propio): ";
        public const string PREFIJO_NO_RECOGIDO = "No recogido (reparto propio): ";
        public const string PREFIJO_ANULADA = "Recogida anulada: ";
        /// <summary>El Estado de la respuesta de NoRecogida (EstadoEnvioRepartoDTO.Estado).</summary>
        public const string ESTADO_NO_RECOGIDO = "NoRecogido";
        public const string ESTADO_ANULADA = "Anulada";
        public const string TABLA_MODIFICACIONES_ANULADA = "RecogidaAnulada";
        public const string CODIGO_YA_RECOGIDA = "RECOGIDA_YA_RECOGIDA";

        /// <returns>El error de usted, o null si vale.</returns>
        public static string ValidarTexto(string texto, bool obligatorio, string que)
        {
            if (string.IsNullOrWhiteSpace(texto))
            {
                return obligatorio ? $"{que} es obligatorio." : null;
            }
            return texto.Trim().Length > MAXIMO_TEXTO ? $"{que} no puede pasar de {MAXIMO_TEXTO} caracteres." : null;
        }

        public static string Detalle(string prefijo, string texto)
        {
            string detalle = prefijo + (texto ?? string.Empty).Trim();
            return detalle.Length <= LONGITUD_DETALLE ? detalle : detalle.Substring(0, LONGITUD_DETALLE);
        }

        /// <summary>Lo que va en EnviosAgenciaPruebasEntrega.Texto: las observaciones y, debajo, el comentario de la recogida.</summary>
        public static string TextoPrueba(string observaciones, string comentario)
        {
            string obs = string.IsNullOrWhiteSpace(observaciones) ? null : observaciones.Trim();
            string com = string.IsNullOrWhiteSpace(comentario) ? null : "Comentario: " + comentario.Trim();
            return obs == null ? com : com == null ? obs : obs + "\n" + com;
        }

        /// <summary>
        /// Lo que impide quitar la parada (no recogerla o anularla): sus bultos NR ya están en una furgoneta o la recogida ya está abonada.
        /// Null: se puede.
        /// </summary>
        public static string MotivoYaRecogida(string documento, SeguimientoNotaRecogida seguimiento)
        {
            if (seguimiento == null)
            {
                return null;
            }
            if (seguimiento.PedidoAbono.HasValue)
            {
                return $"La recogida {documento} ya está abonada (abono {seguimiento.PedidoAbono.Value}): no se puede quitar del reparto.";
            }
            return seguimiento.BultosRecogidos
                ? $"Ya hay bultos de la recogida {documento} recogidos en una furgoneta: entréguela como recogida o descárguelos antes."
                : null;
        }
    }

    /// <summary>Punto 5: lo que la parada de una recogida necesita fuera del reparto (seguimiento, cerrar la parada, avisos de la solicitud).</summary>
    public interface IRecogidasParadaReparto
    {
        /// <summary>Abono y bultos NR de la nota (empresa 1). Null si no se sabe.</summary>
        Task<SeguimientoNotaRecogida> LeerSeguimiento(int pedido);
        /// <summary>
        /// En UNA transacción: el envío a Cerrado a mano con <paramref name="detalle"/> (solo si sigue Tramitado o Incidentado), su historia y
        /// una fila en Modificaciones. False si el envío ya no estaba en reparto (nada cambia).
        /// </summary>
        Task<bool> CerrarParada(int envio, string detalle, IList<EnvioHistoria> historia, string tabla, string anterior, string nuevo, string usuario);
        /// <summary>Aviso a quien pidió la recogida (si viene de una solicitud). Nunca lanza.</summary>
        Task AvisarSolicitud(int pedido, bool recogidaHecha, string texto, string usuario);
        /// <summary>La solicitud de esa nota (si la hay) queda Anulada y se avisa. Nunca lanza.</summary>
        Task AnularSolicitud(int pedido, string motivo, string usuario);
    }

    /// <summary>Punto 5: con su propio contexto (como el resto de piezas que no viven en el del reparto).</summary>
    public class RecogidasParadaRepartoSql : IRecogidasParadaReparto
    {
        private const string SQL_CERRAR = @"
UPDATE dbo.EnviosAgencia SET Estado = @Estado, DetalleEstado = @Detalle, Usuario = @Usuario
WHERE Numero = @Numero AND Agencia = @Agencia AND Estado IN (@Tramitado, @Incidentado);";
        private const string SQL_HISTORIA = @"
INSERT INTO dbo.EnviosHistoria (NumeroEnvio, Campo, ValorAnterior, Observaciones, Usuario, FechaModificacion)
VALUES (@NumeroEnvio, @Campo, @ValorAnterior, @Observaciones, @UsuarioHistoria, @Fecha);";
        private const string SQL_RASTRO = "INSERT INTO dbo.Modificaciones (Tabla, Anterior, Nuevo, Usuario) VALUES (@Tabla, @Anterior, @Nuevo, @UsuarioRastro);";

        private static ServicioRecogidasSolicitadas Solicitudes(NVEntities db)
            => new ServicioRecogidasSolicitadas(new RepositorioRecogidasSolicitadas(db), new AvisosRecogidasSolicitadas());

        public async Task<SeguimientoNotaRecogida> LeerSeguimiento(int pedido)
        {
            using (var db = new NVEntities())
            {
                return await new RepositorioRecogidasSolicitadas(db).LeerSeguimiento(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido).ConfigureAwait(false);
            }
        }

        public async Task<bool> CerrarParada(int envio, string detalle, IList<EnvioHistoria> historia, string tabla, string anterior, string nuevo, string usuario)
        {
            using (var db = new NVEntities())
            using (DbContextTransaction transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    int filas = await db.Database.ExecuteSqlCommandAsync(SQL_CERRAR,
                        new SqlParameter("@Estado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_CERRADO_A_MANO },
                        new SqlParameter("@Detalle", SqlDbType.VarChar, 100) { Value = detalle },
                        new SqlParameter("@Usuario", SqlDbType.VarChar, 50) { Value = Recortar(usuario, 50) },
                        new SqlParameter("@Numero", SqlDbType.Int) { Value = envio },
                        new SqlParameter("@Agencia", SqlDbType.Int) { Value = Constantes.Agencias.AGENCIA_REPARTO_PROPIO },
                        new SqlParameter("@Tramitado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_TRAMITADO },
                        new SqlParameter("@Incidentado", SqlDbType.SmallInt) { Value = Constantes.Agencias.ESTADO_INCIDENTADO }).ConfigureAwait(false);
                    if (filas != 1)
                    {
                        transaccion.Rollback();
                        return false;
                    }
                    foreach (EnvioHistoria fila in historia ?? new List<EnvioHistoria>())
                    {
                        NVEntities.AcotarHistoriaEnvio(fila);
                        _ = await db.Database.ExecuteSqlCommandAsync(SQL_HISTORIA,
                            new SqlParameter("@NumeroEnvio", SqlDbType.Int) { Value = fila.NumeroEnvio },
                            new SqlParameter("@Campo", SqlDbType.NVarChar, 50) { Value = fila.Campo ?? string.Empty },
                            new SqlParameter("@ValorAnterior", SqlDbType.NVarChar, 50) { Value = fila.ValorAnterior ?? string.Empty },
                            new SqlParameter("@Observaciones", SqlDbType.NVarChar, -1) { Value = (object)fila.Observaciones ?? DBNull.Value },
                            new SqlParameter("@UsuarioHistoria", SqlDbType.VarChar, 50) { Value = Recortar(fila.Usuario, 50) },
                            new SqlParameter("@Fecha", SqlDbType.DateTime) { Value = fila.FechaModificacion }).ConfigureAwait(false);
                    }
                    _ = await db.Database.ExecuteSqlCommandAsync(SQL_RASTRO,
                        new SqlParameter("@Tabla", SqlDbType.NVarChar, 50) { Value = tabla },
                        new SqlParameter("@Anterior", SqlDbType.NVarChar, -1) { Value = (object)anterior ?? DBNull.Value },
                        new SqlParameter("@Nuevo", SqlDbType.NVarChar, -1) { Value = (object)nuevo ?? DBNull.Value },
                        new SqlParameter("@UsuarioRastro", SqlDbType.NVarChar, 50) { Value = Recortar(usuario, 50) }).ConfigureAwait(false);
                    transaccion.Commit();
                    return true;
                }
                catch (Exception)
                {
                    transaccion.RollbackSeguro();
                    throw;
                }
            }
        }

        public async Task AvisarSolicitud(int pedido, bool recogidaHecha, string texto, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    await Solicitudes(db).AlPasarPorLaParada(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido, recogidaHecha, texto, usuario).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido avisar de la parada de la recogida {pedido}: {ex.Message}", ex));
            }
        }

        public async Task AnularSolicitud(int pedido, string motivo, string usuario)
        {
            try
            {
                using (var db = new NVEntities())
                {
                    await Solicitudes(db).AlAnularRecogidaEnReparto(Constantes.Empresas.EMPRESA_POR_DEFECTO, pedido, motivo, usuario).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] La recogida {pedido} está anulada, pero no se ha podido anular su solicitud: {ex.Message}", ex));
            }
        }

        private static string Recortar(string valor, int maximo) => valor == null ? string.Empty : valor.Length <= maximo ? valor : valor.Substring(0, maximo);
    }

    /// <summary>Punto 5: «No se ha recogido», el aviso del comentario y «Anular recogida».</summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Punto 5: seguimiento, cierre de la parada y avisos de la solicitud. Null: sin ellos (NoRecogida no mira el abono; Anular da 400).</summary>
        public IRecogidasParadaReparto Recogidas { get; set; }

        private async Task AvisarSolicitudRecogida(int? pedido, bool recogidaHecha, string texto, string usuario)
        {
            if (!pedido.HasValue || Recogidas == null)
            {
                return;
            }
            try
            {
                await Recogidas.AvisarSolicitud(pedido.Value, recogidaHecha, texto, usuario).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido avisar de la parada de la recogida {pedido}: {ex.Message}", ex));
            }
        }

        private async Task<SeguimientoNotaRecogida> SeguimientoRecogida(int pedido)
        {
            if (Recogidas == null)
            {
                return null;
            }
            try
            {
                return await Recogidas.LeerSeguimiento(pedido).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto] No se ha podido leer el seguimiento de la recogida {pedido}: {ex.Message}", ex));
                return null;
            }
        }

        /// <summary>
        /// POST api/RepartoPropio/Envios/{envioId}/NoRecogida: el repartidor no ha podido recoger la parada de una nota de recogida (motivo
        /// obligatorio). Sale del reparto (Cerrado a mano, «No recogido (reparto propio): motivo»), sin bultos NR. Idempotente por la clave.
        /// </summary>
        public async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> NoRecogida(int envioId, NoRecogidaRepartoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await NoRecogidaEnvio(envioId, peticion, usuario).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok)
            {
                resultado.Contenido.Estado = ReglasParadaRecogida.ESTADO_NO_RECOGIDO;
                if (resultado.Contenido.EsNueva)
                {
                    await ApuntarTiempoParada(envioId, new DatosTiempoParada
                    {
                        Clave = peticion.ClaveIdempotencia,
                        Resultado = TiempoParadaReparto.RESULTADO_INCIDENCIA,
                        Fin = peticion.FechaHora ?? ahora(),
                        Ir = peticion.FechaHoraIr,
                        Llegada = peticion.FechaHoraLlegada,
                        Latitud = peticion.Latitud,
                        Longitud = peticion.Longitud
                    }, usuario).ConfigureAwait(false);
                    await AvisarSolicitudRecogida(resultado.Contenido.EnvioEntregado?.Pedido, false, peticion.Motivo.Trim(), usuario).ConfigureAwait(false);
                }
            }
            Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_INCIDENCIA, origen);
            return resultado;
        }

        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> NoRecogidaEnvio(int envioId, NoRecogidaRepartoDTO peticion, string usuario)
        {
            if (NucleoParadasReparto.EsReposicion(envioId))
            {
                return Invalido("«No se ha recogido» solo vale para las notas de recogida.");
            }
            if (!repositorio.ScriptAplicado())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia.");
            }
            string error = ReglasParadaRecogida.ValidarTexto(peticion.Motivo, true, "El motivo");
            if (error != null)
            {
                return Invalido(error);
            }
            string motivo = peticion.Motivo.Trim();
            EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
            if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
            {
                return NoEncontrado(envioId);
            }
            ResultadoReparto<EstadoEnvioRepartoDTO> repetida = await YaRegistrada(peticion.ClaveIdempotencia, envioId, PruebaEntregaReparto.TIPO_INCIDENCIA).ConfigureAwait(false);
            if (repetida != null)
            {
                return repetida;
            }
            if (envio.Estado == Constantes.Agencias.ESTADO_ENTREGADO)
            {
                return Conflicto($"El envío {envio.Numero} ya está recogido.");
            }
            if (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO)
            {
                return Conflicto(NoEstaEnReparto(envio));
            }
            NotaParadaReparto nota = await NotaDe(envio.Pedido).ConfigureAwait(false);
            if (nota?.EsRecogida != true)
            {
                return Invalido("«No se ha recogido» solo vale para las notas de recogida.");
            }
            string yaRecogida = ReglasParadaRecogida.MotivoYaRecogida(nota.CodigoDocumento, await SeguimientoRecogida(nota.Pedido).ConfigureAwait(false));
            if (yaRecogida != null)
            {
                return new ResultadoReparto<EstadoEnvioRepartoDTO> { Tipo = TipoResultadoReparto.Conflicto, Mensaje = yaRecogida, Codigo = ReglasParadaRecogida.CODIGO_YA_RECOGIDA };
            }

            DateTime fechaHora = peticion.FechaHora ?? ahora();
            var prueba = new PruebaEntregaReparto
            {
                EnvioId = envio.Numero,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                Tipo = PruebaEntregaReparto.TIPO_INCIDENCIA,
                FechaHora = fechaHora,
                Motivo = IncidenciaRepartoDTO.OTRO,
                Texto = "No se ha recogido: " + motivo,
                Latitud = peticion.Latitud,
                Longitud = peticion.Longitud,
                Usuario = usuario,
                FechaRegistro = ahora()
            };
            List<EnvioHistoria> historia = Historia(envio, "Reparto propio: no se ha recogido. Motivo: " + motivo, usuario);
            envio.Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO;
            envio.DetalleEstado = ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_NO_RECOGIDO, motivo);
            envio.Usuario = usuario;
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await Registrar(prueba, null, envio, historia, null, usuario, ReglasParadaRecogida.ESTADO_NO_RECOGIDO).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok && resultado.Contenido != null && resultado.Contenido.EsNueva)
            {
                resultado.Contenido.EnvioEntregado = envio;
            }
            return resultado;
        }

        /// <summary>
        /// POST api/RepartoPropio/Recogidas/{pedido}/Anular: una nota de recogida YA en reparto que ya no hace falta. La parada sale del
        /// reparto (Cerrado a mano, «Recogida anulada: motivo») con historia y rastro en Modificaciones; si viene de una solicitud, la
        /// solicitud queda Anulada. 409 si sus bultos NR ya están en una furgoneta o está abonada. La nota (sus líneas) no se toca.
        /// </summary>
        public async Task<ResultadoReparto<ResultadoAnularRecogidaDTO>> AnularRecogida(int pedido, AnularRecogidaRepartoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            string error = ReglasParadaRecogida.ValidarTexto(peticion?.Motivo, true, "El motivo");
            if (error != null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Invalido, error);
            }
            if (Recogidas == null || paradas.Notas == null || paradas.NotasParada == null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Invalido, "No se pueden anular recogidas desde aquí. Avise a Informática.");
            }
            string motivo = peticion.Motivo.Trim();
            string documento = CodigoNotaEntrega.Documento(true, pedido);
            NotaParadaReparto nota = await NotaDe(pedido).ConfigureAwait(false);
            if (nota?.EsRecogida != true)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe la nota de recogida {documento}.");
            }
            ResolucionCodigoNota resolucion = await paradas.Notas.Resolver(documento).ConfigureAwait(false);
            if (resolucion?.EnvioId == null)
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, resolucion?.EnvioEntregado != null
                    ? $"La recogida {documento} ya está hecha: no se puede anular."
                    : $"La nota {documento} no tiene parada en el reparto: no hay nada que anular aquí (si viene de una solicitud, anule la solicitud).");
            }
            string yaRecogida = ReglasParadaRecogida.MotivoYaRecogida(documento, await SeguimientoRecogida(pedido).ConfigureAwait(false));
            if (yaRecogida != null)
            {
                return new ResultadoReparto<ResultadoAnularRecogidaDTO> { Tipo = TipoResultadoReparto.Conflicto, Mensaje = yaRecogida, Codigo = ReglasParadaRecogida.CODIGO_YA_RECOGIDA };
            }
            EnviosAgencia envio = await repositorio.LeerEnvio(resolucion.EnvioId.Value).ConfigureAwait(false);
            if (envio == null || (envio.Estado != Constantes.Agencias.ESTADO_TRAMITADO && envio.Estado != Constantes.Agencias.ESTADO_INCIDENTADO))
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, $"La parada de la recogida {documento} ya no está en el reparto: actualice la lista.");
            }
            string detalle = ReglasParadaRecogida.Detalle(ReglasParadaRecogida.PREFIJO_ANULADA, motivo);
            List<EnvioHistoria> historia = Historia(envio, $"Recogida {documento} anulada: {motivo}", usuario);
            string anterior = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, envio.Estado, DetalleEstado = envio.DetalleEstado?.Trim() });
            string nuevo = JsonConvert.SerializeObject(new { Pedido = pedido, Envio = envio.Numero, Estado = Constantes.Agencias.ESTADO_CERRADO_A_MANO, DetalleEstado = detalle, Motivo = motivo });
            if (!await Recogidas.CerrarParada(envio.Numero, detalle, historia, ReglasParadaRecogida.TABLA_MODIFICACIONES_ANULADA, anterior, nuevo, usuario).ConfigureAwait(false))
            {
                return ResultadoReparto<ResultadoAnularRecogidaDTO>.Mal(TipoResultadoReparto.Conflicto, $"La parada de la recogida {documento} ha cambiado mientras: actualice la lista.");
            }
            await Recogidas.AnularSolicitud(pedido, motivo, usuario).ConfigureAwait(false);
            Avisar(TipoResultadoReparto.Ok, usuario, RepartoCambiadoDTO.MOTIVO_PARADA, origen);
            return ResultadoReparto<ResultadoAnularRecogidaDTO>.Bien(new ResultadoAnularRecogidaDTO
            {
                Pedido = pedido,
                CodigoDocumento = documento,
                EnvioId = envio.Numero,
                Estado = ReglasParadaRecogida.ESTADO_ANULADA,
                Mensaje = $"Recogida {documento} anulada: la parada {envio.Numero} ya no está en el reparto."
            });
        }
    }
}
