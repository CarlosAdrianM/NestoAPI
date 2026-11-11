using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;
using Newtonsoft.Json;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#772 (decisiones de Carlos del 31/10/26): lo que lleva cada furgoneta, bulto a bulto. Cada bulto tiene su historia
    /// en RepartoBultosMovimientos (<see cref="IRepositorioFurgoneta"/>):
    /// <list type="bullet">
    /// <item><b>Cargado</b>: lo apunta «Cargar» además de lo de siempre (<see cref="AnotarCarga"/>), con la misma clave.</item>
    /// <item><b>Recogido</b>: <see cref="Recoger"/> (NR de una nota de recogida y R de una reposición de una tienda a Algete). Con el
    /// último bulto de la reposición, queda «Recogida» sola (como POST api/Reposiciones/Recogida).</item>
    /// <item><b>Entregado</b>: al entregar la parada (<see cref="AnotarEntrega"/>): los leídos o, sin leer ninguno, todos los de esa
    /// parada que llevaba (leer es opcional mientras <see cref="PARAMETRO_ESCANEO_ENTREGA_OBLIGATORIO"/> esté apagado).</item>
    /// <item><b>Descargado</b>: <see cref="Descargar"/> (el repartidor por la tarde) o el almacén al leerlo
    /// (<see cref="RedSeguridadFurgoneta"/>).</item>
    /// </list>
    /// «Lo que lleva encima» = último movimiento Cargado o Recogido suyo (<see cref="NucleoFurgoneta"/>). Lo que ya existía
    /// (EnviosAgenciaCargaBultos, RepartoParadasReposicionBultos) sigue igual: sin el script, todo como antes.
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        public const string MENSAJE_FALTA_SCRIPT_FURGONETA = "Falta lanzar el script Scripts/Issue772_FurgonetaBultos.sql (lo que lleva cada furgoneta): " +
            "hasta entonces no se puede recoger, descargar ni terminar la jornada. Avise a Informática.";
        public const string CODIGO_NO_EN_FURGONETA = "NO_EN_FURGONETA";
        /// <summary>Descargar con SinLeer de un bulto que va en la furgoneta de otro repartidor (409).</summary>
        public const string CODIGO_EN_OTRA_FURGONETA = "EN_OTRA_FURGONETA";
        /// <summary>El rastro en Modificaciones (Tabla) de cada bulto quitado sin leer: Anterior = su último movimiento, Nuevo = el Descargado.</summary>
        public const string TABLA_MODIFICACIONES_SIN_LEER = "FurgonetaDescargaSinLeer";
        /// <summary>Bajo «(defecto)»: "1" = hay que leer los bultos al entregar. Apagado (Carlos 31/10/26: «hasta que nos acostumbremos»).</summary>
        public const string PARAMETRO_ESCANEO_ENTREGA_OBLIGATORIO = "RepartoEscaneoEntregaObligatorio";
        public const string MENSAJE_ESCANEO_OBLIGATORIO = "Lea los bultos que entrega antes de confirmar la entrega.";
        /// <summary>La furgoneta mira los bultos movidos en los últimos 30 días (lo de antes, ya no se enseña).</summary>
        internal const int DIAS_FURGONETA = 30;

        private bool FurgonetaDisponible() => paradas.Furgoneta != null && paradas.Furgoneta.ScriptAplicado();

        // ---------------------------------------------------------------- la furgoneta

        /// <summary>GET api/RepartoPropio/Furgoneta: lo que lleva encima <paramref name="usuario"/> (o todos, con <paramref name="todos"/>).</summary>
        public async Task<ResultadoReparto<FurgonetaRepartoDTO>> Furgoneta(string usuario, bool todos = false)
        {
            if (!FurgonetaDisponible())
            {
                return ResultadoReparto<FurgonetaRepartoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_FURGONETA);
            }
            if (!todos && string.IsNullOrWhiteSpace(usuario))
            {
                return ResultadoReparto<FurgonetaRepartoDTO>.Mal(TipoResultadoReparto.Invalido, "No se sabe de quién es la furgoneta.");
            }
            List<MovimientoBultoReparto> encima = await EnFurgoneta(todos ? null : usuario).ConfigureAwait(false);
            DateTime? jornada = null;
            if (!todos)
            {
                jornada = (await paradas.Furgoneta.LeerJornadas(ahora().Date).ConfigureAwait(false) ?? new List<JornadaReparto>())
                    .Where(j => MismoUsuario(j.Usuario, usuario))
                    .Select(j => (DateTime?)j.FechaFin)
                    .DefaultIfEmpty(null)
                    .Max();
            }
            return ResultadoReparto<FurgonetaRepartoDTO>.Bien(new FurgonetaRepartoDTO
            {
                Usuario = todos ? null : NombreRepartidor(usuario),
                Total = encima.Count,
                JornadaTerminada = jornada,
                Bultos = encima.Select(NucleoFurgoneta.ADto).ToList()
            });
        }

        private async Task<List<MovimientoBultoReparto>> EnFurgoneta(string usuario)
            => NucleoFurgoneta.EnFurgoneta(await paradas.Furgoneta.LeerUltimos(ahora().Date.AddDays(-DIAS_FURGONETA)).ConfigureAwait(false), usuario);

        // ---------------------------------------------------------------- recoger

        /// <summary>
        /// POST api/RepartoPropio/Recoger: el repartidor lee un bulto que se trae (NR de una nota de recogida o R de una reposición
        /// de una tienda a Algete) y pasa a su furgoneta aunque no sea una parada suya. Con el último bulto de la reposición (N de
        /// Total), la reposición de hoy de esa tienda queda «Recogida» (con su usuario y su hora) y se avisa a Nesto por SignalR.
        /// </summary>
        /// <param name="quien">El usuario de la petición, para marcar la recogida con sus permisos. Null: no se marca.</param>
        public async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> Recoger(MovimientoBultoRepartoDTO peticion, string usuario,
            OrigenPeticionReparto origen = null, IPrincipal quien = null)
        {
            // NestoAPI#844: con dinero de días anteriores en la caja, no se empieza a recoger
            if (ControlCaja != null && peticion != null
                && await ControlCaja.MotivoParaNoEmpezar(usuario, peticion.FechaHora).ConfigureAwait(false) is string motivo)
            {
                return ReglaCajaSinCerrar.Conflicto<ResultadoMovimientoBultoDTO>(motivo);
            }
            ResultadoReparto<ResultadoMovimientoBultoDTO> resultado = await RecogerBulto(peticion, usuario, origen, quien).ConfigureAwait(false);
            if (resultado.Contenido?.YaEstaba != true)
            {
                Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_FURGONETA, origen);
            }
            return resultado;
        }

        private async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> RecogerBulto(MovimientoBultoRepartoDTO peticion, string usuario,
            OrigenPeticionReparto origen, IPrincipal quien)
        {
            ResultadoReparto<ResultadoMovimientoBultoDTO> invalida = ValidarMovimiento(peticion, usuario, "la recogida");
            if (invalida != null)
            {
                return invalida;
            }
            CodigoBultoFurgoneta codigo = CodigoBultoFurgoneta.Leer(peticion.Codigo);
            if (codigo == null)
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, $"El código «{peticion.Codigo?.Trim()}» no es de un bulto que se recoja (NR… de una nota de recogida o R… de una reposición).");
            }
            if (codigo.TipoCodigo == CodigoBultoFurgoneta.RP)
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, $"El bulto {codigo.Codigo} es de un envío: se carga en Algete con «Cargar furgoneta».");
            }

            MovimientoBultoReparto previa = await paradas.Furgoneta.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                return previa.Codigo == codigo.Codigo && previa.Tipo == TiposMovimientoBulto.RECOGIDO
                    ? ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaRecogida(previa, usuario, false, false).ConfigureAwait(false))
                    : MalMovimiento(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para el bulto {previa.Codigo}. Cada lectura lleva su propia clave.");
            }

            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string tiendaOrigen = null;
            var nuevo = new MovimientoBultoReparto
            {
                Codigo = codigo.Codigo,
                TipoCodigo = codigo.TipoCodigo,
                Bulto = codigo.Bulto,
                Pedido = codigo.Pedido,
                NumTraspaso = codigo.Traspaso,
                Tipo = TiposMovimientoBulto.RECOGIDO,
                Origen = OrigenesMovimientoBulto.REPARTIDOR
            };
            if (codigo.TipoCodigo == CodigoBultoFurgoneta.NR)
            {
                ResolucionCodigoNota nota = paradas.Notas == null ? null : await paradas.Notas.Resolver(codigo.Codigo).ConfigureAwait(false);
                if (nota == null || !nota.EsNota || !nota.EsRecogida)
                {
                    return MalMovimiento(TipoResultadoReparto.NoEncontrado, $"El código «{codigo.Codigo}» no es de ninguna nota de recogida.");
                }
                nuevo.EnvioId = nota.EnvioId ?? nota.EnvioEntregado;
                EnviosAgencia envio = nuevo.EnvioId.HasValue ? await repositorio.LeerEnvio(nuevo.EnvioId.Value).ConfigureAwait(false) : null;
                nuevo.Total = envio == null ? (int?)null : Math.Max((int)envio.Bultos, 1);
                nuevo.Descripcion = $"Recogida {codigo.Pedido}" + (string.IsNullOrWhiteSpace(envio?.Nombre) ? string.Empty : $": {envio.Nombre.Trim()}");
            }
            else
            {
                if (paradas.BultosReposicion == null)
                {
                    return MalMovimiento(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_PARADAS);
                }
                FilaBultoReposicion etiqueta = await paradas.BultosReposicion.LeerPorCodigo(empresa, codigo.Codigo).ConfigureAwait(false);
                if (etiqueta == null)
                {
                    return MalMovimiento(TipoResultadoReparto.NoEncontrado, $"El bulto {codigo.Codigo} no es de ninguna reposición enviada: compruebe la etiqueta.");
                }
                OrigenDestinoReposicion od = await paradas.BultosReposicion.LeerOrigenDestino(empresa, codigo.Traspaso.Value).ConfigureAwait(false);
                string destino = od?.Destino?.Trim().ToUpperInvariant();
                tiendaOrigen = od?.Origen?.Trim().ToUpperInvariant();
                if (destino != Constantes.Almacenes.ALGETE)
                {
                    return MalMovimiento(TipoResultadoReparto.Conflicto, destino == null
                        ? $"No se sabe adónde va la reposición {codigo.Traspaso}: no se puede recoger."
                        : $"El bulto {codigo.Codigo} va a {NombreTienda(destino)}: se carga en Algete con «Cargar furgoneta».");
                }
                if (etiqueta.FechaLectura.HasValue)
                {
                    return MalMovimiento(TipoResultadoReparto.Conflicto, $"La reposición {codigo.Traspaso} ya se ha recibido en Algete: no hay que recogerla.");
                }
                nuevo.Total = Math.Max((int)etiqueta.Total, codigo.Bulto);
                nuevo.Descripcion = $"Reposición {codigo.Traspaso} {NombreTienda(tiendaOrigen)} → Algete";
            }

            List<MovimientoBultoReparto> historia = await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Codigos = new[] { codigo.Codigo } })
                .ConfigureAwait(false) ?? new List<MovimientoBultoReparto>();
            MovimientoBultoReparto ultimo = NucleoFurgoneta.Ultimo(historia, codigo.Codigo);
            if (NucleoFurgoneta.LoLleva(ultimo, usuario))
            {
                // El mismo bulto otra vez (otra clave): ya lo lleva; no se apunta dos veces.
                return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaRecogida(ultimo, usuario, true, false).ConfigureAwait(false));
            }
            bool yaCompleta = codigo.TipoCodigo == CodigoBultoFurgoneta.R && await ReposicionCompleta(codigo.Traspaso.Value, nuevo.Total).ConfigureAwait(false);

            DateTime momento = ahora();
            nuevo.Usuario = usuario.Trim();
            nuevo.FechaHora = peticion.FechaHora ?? momento;
            nuevo.FechaRegistro = momento;
            nuevo.Latitud = peticion.Latitud;
            nuevo.Longitud = peticion.Longitud;
            nuevo.ClaveIdempotencia = peticion.ClaveIdempotencia;
            _ = await paradas.Furgoneta.Registrar(new[] { nuevo }).ConfigureAwait(false);

            bool recogida = false;
            if (codigo.TipoCodigo == CodigoBultoFurgoneta.R)
            {
                // Con el ÚLTIMO bulto (N de Total), la reposición queda «Recogida». Con 6 de 7, todavía no.
                if (!yaCompleta && await ReposicionCompleta(codigo.Traspaso.Value, nuevo.Total).ConfigureAwait(false))
                {
                    recogida = await MarcarReposicionRecogida(empresa, tiendaOrigen, quien).ConfigureAwait(false);
                }
                AvisarReposiciones(tiendaOrigen, usuario, origen);
            }
            ResultadoMovimientoBultoDTO respuesta = await RespuestaRecogida(nuevo, usuario, false, recogida).ConfigureAwait(false);
            return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(respuesta);
        }

        /// <summary>¿Tienen ya todos los bultos (1..Total) del traspaso un «Recogido»?</summary>
        private async Task<bool> ReposicionCompleta(int traspaso, int? total)
        {
            if (!(total > 0))
            {
                return false;
            }
            return await BultosRecogidosDeTraspaso(traspaso).ConfigureAwait(false) >= total.Value;
        }

        private async Task<int> BultosRecogidosDeTraspaso(int traspaso)
            => (await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Traspasos = new[] { traspaso } }).ConfigureAwait(false)
                ?? new List<MovimientoBultoReparto>())
                .Where(m => m.TipoCodigo == CodigoBultoFurgoneta.R && m.NumTraspaso == traspaso && m.Tipo == TiposMovimientoBulto.RECOGIDO)
                .Select(m => m.Bulto).Distinct().Count();

        private async Task<bool> MarcarReposicionRecogida(string empresa, string tienda, IPrincipal quien)
        {
            if (paradas.RecogidaReposiciones == null || quien == null || string.IsNullOrWhiteSpace(tienda))
            {
                return false;
            }
            try
            {
                return await paradas.RecogidaReposiciones.Marcar(empresa, tienda, quien).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo marcar como recogida la reposición de {tienda}: {ex.Message}", ex));
                return false;
            }
        }

        private async Task<ResultadoMovimientoBultoDTO> RespuestaRecogida(MovimientoBultoReparto m, string usuario, bool yaEstaba, bool reposicionRecogida)
        {
            int? recogidos = null;
            if (m.TipoCodigo == CodigoBultoFurgoneta.R && m.NumTraspaso.HasValue)
            {
                recogidos = await BultosRecogidosDeTraspaso(m.NumTraspaso.Value).ConfigureAwait(false);
            }
            else if (m.TipoCodigo == CodigoBultoFurgoneta.NR && m.Pedido.HasValue)
            {
                recogidos = (await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Pedidos = new[] { m.Pedido.Value } }).ConfigureAwait(false)
                    ?? new List<MovimientoBultoReparto>())
                    .Where(x => x.TipoCodigo == CodigoBultoFurgoneta.NR && x.Tipo == TiposMovimientoBulto.RECOGIDO)
                    .Select(x => x.Bulto).Distinct().Count();
            }
            ResultadoMovimientoBultoDTO dto = await Respuesta(m, usuario, yaEstaba).ConfigureAwait(false);
            dto.BultosRecogidos = recogidos;
            dto.ReposicionRecogida = reposicionRecogida;
            string cuantos = recogidos.HasValue && m.Total.HasValue ? $" ({recogidos} de {m.Total} {(m.Total == 1 ? "bulto" : "bultos")})" : string.Empty;
            dto.Mensaje = yaEstaba
                ? $"El bulto {m.Codigo} ya está en su furgoneta{cuantos}."
                : reposicionRecogida
                    ? $"Bulto {m.Codigo} recogido{cuantos}: la reposición {m.NumTraspaso} queda recogida."
                    : $"Bulto {m.Codigo} recogido{cuantos}.";
            return dto;
        }

        // ---------------------------------------------------------------- descargar

        /// <summary>
        /// POST api/RepartoPropio/Descargar: por la tarde, el repartidor lee cada bulto que saca de la furgoneta (lo contrario de
        /// «Cargar furgoneta»). Vale cualquier bulto que esté en una furgoneta, aunque sea de otro. Ya descargado: 200 YaEstaba. En
        /// ninguna furgoneta: 409 <see cref="CODIGO_NO_EN_FURGONETA"/>.
        /// </summary>
        public async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> Descargar(MovimientoBultoRepartoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            ResultadoReparto<ResultadoMovimientoBultoDTO> resultado = await DescargarBulto(peticion, usuario, origen).ConfigureAwait(false);
            if (resultado.Contenido?.YaEstaba != true)
            {
                Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_FURGONETA, origen);
            }
            return resultado;
        }

        private async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> DescargarBulto(MovimientoBultoRepartoDTO peticion, string usuario, OrigenPeticionReparto origen)
        {
            ResultadoReparto<ResultadoMovimientoBultoDTO> invalida = ValidarMovimiento(peticion, usuario, "la descarga");
            if (invalida != null)
            {
                return invalida;
            }
            if (peticion.SinLeer)
            {
                return await DescargarSinLeer(peticion, usuario).ConfigureAwait(false);
            }
            CodigoBultoFurgoneta codigo = CodigoBultoFurgoneta.Leer(peticion.Codigo);
            if (codigo == null)
            {
                // Incidencia 1190 (11/11/26): lo que vale para cargar vale para descargar. El papel NE{pedido}, a los bultos de su envío.
                if (CodigoNotaEntrega.TryLeer(peticion.Codigo, out string prefijo, out _, out _) && prefijo == CodigoNotaEntrega.PREFIJO_ENTREGA)
                {
                    return await DescargarNotaEntrega(peticion, usuario).ConfigureAwait(false);
                }
                return MalMovimiento(TipoResultadoReparto.Invalido, $"El código «{peticion.Codigo?.Trim()}» no es de un bulto del reparto (RP…, NE…, NR… o R…).");
            }
            MovimientoBultoReparto previa = await paradas.Furgoneta.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                return previa.Codigo == codigo.Codigo && previa.Tipo == TiposMovimientoBulto.DESCARGADO
                    ? ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaDescarga(previa, usuario, null, false).ConfigureAwait(false))
                    : MalMovimiento(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para el bulto {previa.Codigo}. Cada lectura lleva su propia clave.");
            }
            List<MovimientoBultoReparto> historia = await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Codigos = new[] { codigo.Codigo } })
                .ConfigureAwait(false) ?? new List<MovimientoBultoReparto>();
            MovimientoBultoReparto ultimo = NucleoFurgoneta.Ultimo(historia, codigo.Codigo);
            bool enFurgoneta = ultimo != null && TiposMovimientoBulto.DejaEnFurgoneta(ultimo.Tipo);
            // Incidencia 1189 (11/11/26): la parada de una recogida no lleva bulto propio; un RP suyo que siga en una furgoneta (de
            // antes de no apuntarlo al cargar) sale con su NR, aunque la NR ya estuviera descargada.
            List<MovimientoBultoReparto> fantasmas = codigo.TipoCodigo == CodigoBultoFurgoneta.NR
                ? await RpSinBultoDeLaRecogida(ultimo?.EnvioId, codigo.Pedido.Value).ConfigureAwait(false)
                : new List<MovimientoBultoReparto>();
            if (!enFurgoneta && fantasmas.Count == 0)
            {
                if (ultimo != null && ultimo.Tipo == TiposMovimientoBulto.DESCARGADO)
                {
                    return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaDescarga(ultimo, usuario, null, true).ConfigureAwait(false));
                }
                return NoEnFurgoneta(codigo.Codigo, ultimo);
            }
            DateTime momento = ahora();
            var nuevos = new List<MovimientoBultoReparto>();
            MovimientoBultoReparto principal = null;
            if (enFurgoneta)
            {
                principal = Descargado(ultimo, peticion, usuario, momento, peticion.ClaveIdempotencia);
                nuevos.Add(principal);
            }
            // Los RP sin bulto, siempre con clave derivada: un reintento no los apunta dos veces
            nuevos.AddRange(fantasmas.Select(f => Descargado(f, peticion, usuario, momento,
                NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, f.Codigo, TiposMovimientoBulto.DESCARGADO))));
            _ = await paradas.Furgoneta.Registrar(nuevos).ConfigureAwait(false);
            if (principal != null && principal.TipoCodigo == CodigoBultoFurgoneta.R && principal.NumTraspaso.HasValue && paradas.BultosReposicion != null)
            {
                OrigenDestinoReposicion od = await paradas.BultosReposicion.LeerOrigenDestino(Constantes.Empresas.EMPRESA_POR_DEFECTO, principal.NumTraspaso.Value).ConfigureAwait(false);
                AvisarReposiciones(od?.Origen, usuario, origen);
            }
            ResultadoMovimientoBultoDTO dto;
            if (principal != null)
            {
                dto = await RespuestaDescarga(principal, usuario, ultimo.Usuario, false).ConfigureAwait(false);
            }
            else
            {
                dto = await Respuesta(nuevos[0], usuario, false).ConfigureAwait(false);
                dto.Mensaje = ultimo?.Tipo == TiposMovimientoBulto.DESCARGADO
                    ? $"El bulto {codigo.Codigo} ya estaba descargado."
                    : $"El bulto {codigo.Codigo} no está en ninguna furgoneta.";
            }
            dto.Descargados = nuevos.Select(n => n.Codigo).ToList();
            if (fantasmas.Count > 0)
            {
                string lista = NucleoFurgoneta.ListaCorta(fantasmas.Select(f => f.Codigo));
                dto.Mensaje += principal != null
                    ? $" También sale {lista}: la parada de una recogida no lleva bulto propio."
                    : $" Sale de la furgoneta {lista}: la parada de una recogida no lleva bulto propio.";
            }
            return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(dto);
        }

        private static MovimientoBultoReparto Descargado(MovimientoBultoReparto ultimo, MovimientoBultoRepartoDTO peticion, string usuario, DateTime momento, Guid clave)
        {
            MovimientoBultoReparto nuevo = Copia(ultimo, TiposMovimientoBulto.DESCARGADO, usuario.Trim(), peticion.FechaHora ?? momento, momento,
                OrigenesMovimientoBulto.REPARTIDOR, clave);
            nuevo.Latitud = peticion.Latitud;
            nuevo.Longitud = peticion.Longitud;
            return nuevo;
        }

        private static ResultadoReparto<ResultadoMovimientoBultoDTO> NoEnFurgoneta(string codigo, MovimientoBultoReparto ultimo)
            => new ResultadoReparto<ResultadoMovimientoBultoDTO>
            {
                Tipo = TipoResultadoReparto.Conflicto,
                Mensaje = ultimo == null
                    ? $"El bulto {codigo} no está en ninguna furgoneta: no se ha cargado ni recogido."
                    : $"El bulto {codigo} no está en ninguna furgoneta: se entregó el {ultimo.FechaHora:dd/MM} a las {ultimo.FechaHora:H:mm} ({NombreRepartidor(ultimo.Usuario)}).",
                Codigo = CODIGO_NO_EN_FURGONETA,
                Detalles = new { codigo, ultimo = ultimo?.Tipo }
            };

        /// <summary>
        /// Los RP de la parada de esa nota de recogida que siguen en alguna furgoneta. Una recogida no tiene bulto propio (no existe
        /// hasta recogerlo, con su NR): esos RP son de cuando «Cargar» los apuntaba. Solo si la parada es de verdad una recogida.
        /// </summary>
        private async Task<List<MovimientoBultoReparto>> RpSinBultoDeLaRecogida(int? envioId, int pedido)
        {
            if (!envioId.HasValue && paradas.Notas != null)
            {
                ResolucionCodigoNota nota = await paradas.Notas.Resolver(CodigoNotaEntrega.Documento(true, pedido)).ConfigureAwait(false);
                envioId = nota?.EsNota == true ? nota.EnvioId ?? nota.EnvioEntregado : null;
            }
            if (!(envioId > 0) || (await NotaDeEnvio(envioId.Value).ConfigureAwait(false))?.EsRecogida != true)
            {
                return new List<MovimientoBultoReparto>();
            }
            return NucleoFurgoneta.Ultimos(await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Envios = new[] { envioId.Value } }).ConfigureAwait(false))
                .Where(m => m.TipoCodigo == CodigoBultoFurgoneta.RP && TiposMovimientoBulto.DejaEnFurgoneta(m.Tipo))
                .OrderBy(m => m.Bulto)
                .ToList();
        }

        /// <summary>
        /// Incidencia 1190 (11/11/26): Descargar con el papel NE{pedido} (una entrega sin etiqueta RP), como en Cargar: los bultos RP
        /// de su envío que sigan en una furgoneta, TODOS (el papel es del envío entero). El primero lleva la clave de la petición y
        /// los demás una derivada: un reintento no apunta nada dos veces.
        /// </summary>
        private async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> DescargarNotaEntrega(MovimientoBultoRepartoDTO peticion, string usuario)
        {
            string documento = peticion.Codigo.Trim().ToUpperInvariant();
            ResolucionCodigoNota nota = paradas.Notas == null ? null : await paradas.Notas.Resolver(documento).ConfigureAwait(false);
            if (nota == null || !nota.EsNota)
            {
                return MalMovimiento(TipoResultadoReparto.NoEncontrado, $"El código «{documento}» no es de ninguna nota de entrega.");
            }
            int? envioId = nota.EnvioId ?? nota.EnvioEntregado;
            if (!envioId.HasValue)
            {
                return new ResultadoReparto<ResultadoMovimientoBultoDTO>
                {
                    Tipo = TipoResultadoReparto.Conflicto,
                    Mensaje = $"La nota {documento} no está en el reparto, así que no figura en ninguna furgoneta: no tiene que descargarla. Deje el paquete en el almacén.",
                    Codigo = CODIGO_NO_EN_FURGONETA,
                    Detalles = new { codigo = documento }
                };
            }
            List<MovimientoBultoReparto> bultos = NucleoFurgoneta.Ultimos(await paradas.Furgoneta.LeerHistoria(
                    new ConsultaMovimientosBulto { Envios = new[] { envioId.Value } }).ConfigureAwait(false))
                .Where(m => m.TipoCodigo == CodigoBultoFurgoneta.RP)
                .OrderBy(m => m.Bulto)
                .ToList();
            Guid clave = peticion.ClaveIdempotencia;
            MovimientoBultoReparto previa = await paradas.Furgoneta.LeerPorClave(clave).ConfigureAwait(false);
            if (previa != null)
            {
                if (previa.Tipo != TiposMovimientoBulto.DESCARGADO || previa.EnvioId != envioId)
                {
                    return MalMovimiento(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para el bulto {previa.Codigo}. Cada lectura lleva su propia clave.");
                }
                // El reintento: la misma respuesta, con los que salieron con esta lectura
                List<string> salieron = bultos.Where(b => b.Tipo == TiposMovimientoBulto.DESCARGADO
                        && (b.ClaveIdempotencia == clave || b.ClaveIdempotencia == NucleoFurgoneta.ClaveDerivada(clave, b.Codigo, TiposMovimientoBulto.DESCARGADO)))
                    .Select(b => b.Codigo).ToList();
                return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaNotaEntrega(previa, documento, nota.Pedido, salieron.Count == 0 ? new List<string> { previa.Codigo } : salieron, usuario, null)
                    .ConfigureAwait(false));
            }
            List<MovimientoBultoReparto> enFurgoneta = bultos.Where(b => TiposMovimientoBulto.DejaEnFurgoneta(b.Tipo)).ToList();
            if (enFurgoneta.Count == 0)
            {
                MovimientoBultoReparto descargado = bultos.LastOrDefault(b => b.Tipo == TiposMovimientoBulto.DESCARGADO);
                if (descargado != null && bultos.All(b => b.Tipo != TiposMovimientoBulto.ENTREGADO))
                {
                    ResultadoMovimientoBultoDTO ya = await Respuesta(descargado, usuario, true).ConfigureAwait(false);
                    ya.Pedido = nota.Pedido;
                    ya.Mensaje = $"La nota {documento} ya estaba descargada.";
                    return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(ya);
                }
                EnviosAgencia envio = await repositorio.LeerEnvio(envioId.Value).ConfigureAwait(false);
                string quien = string.IsNullOrWhiteSpace(envio?.Nombre) ? string.Empty : $" ({envio.Nombre.Trim()})";
                return new ResultadoReparto<ResultadoMovimientoBultoDTO>
                {
                    Tipo = TipoResultadoReparto.Conflicto,
                    Mensaje = bultos.Any(b => b.Tipo == TiposMovimientoBulto.ENTREGADO) || envio?.Estado == Constantes.Agencias.ESTADO_ENTREGADO
                        ? $"La nota {documento}{quien} ya está entregada: no figura en ninguna furgoneta y no tiene que descargarla."
                        : $"La nota {documento}{quien} no figura en ninguna furgoneta (no se leyó al cargar): no tiene que descargarla. Deje el paquete en su sitio del almacén.",
                    Codigo = CODIGO_NO_EN_FURGONETA,
                    Detalles = new { codigo = documento, envio = envioId }
                };
            }
            DateTime momento = ahora();
            List<MovimientoBultoReparto> nuevos = enFurgoneta
                .Select((b, i) => Descargado(b, peticion, usuario, momento, i == 0 ? clave : NucleoFurgoneta.ClaveDerivada(clave, b.Codigo, TiposMovimientoBulto.DESCARGADO)))
                .ToList();
            _ = await paradas.Furgoneta.Registrar(nuevos).ConfigureAwait(false);
            string otro = enFurgoneta.Select(b => b.Usuario).FirstOrDefault(u => !MismoUsuario(u, usuario));
            return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaNotaEntrega(nuevos[0], documento, nota.Pedido, nuevos.Select(n => n.Codigo).ToList(), usuario, otro)
                .ConfigureAwait(false));
        }

        private async Task<ResultadoMovimientoBultoDTO> RespuestaNotaEntrega(MovimientoBultoReparto primero, string documento, int pedido, List<string> descargados,
            string usuario, string loLlevabaOtro)
        {
            ResultadoMovimientoBultoDTO dto = await Respuesta(primero, usuario, false).ConfigureAwait(false);
            dto.Pedido = pedido;
            dto.Descargados = descargados;
            string llevaba = loLlevabaOtro == null ? string.Empty : $" (lo llevaba {NombreRepartidor(loLlevabaOtro)})";
            dto.Mensaje = descargados.Count == 1
                ? $"Nota {documento}: bulto {descargados[0]} descargado{llevaba}."
                : $"Nota {documento}: {TextoBultos(descargados.Count)} descargados ({NucleoFurgoneta.ListaCorta(descargados)}){llevaba}.";
            return dto;
        }

        /// <summary>
        /// Incidencias 1189/1190 (11/11/26): Descargar con SinLeer. El repartidor quita de SU furgoneta un bulto de la lista que no
        /// puede leer (sin etiqueta, o un RP que no existe). Solo de la suya (si lo lleva otro, 409 <see cref="CODIGO_EN_OTRA_FURGONETA"/>).
        /// Queda el rastro en Modificaciones (<see cref="TABLA_MODIFICACIONES_SIN_LEER"/>, en la misma transacción) y el aviso en la
        /// campana a Almacén. Idempotente por la clave: un reintento ni deja otro rastro ni vuelve a avisar.
        /// </summary>
        private async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> DescargarSinLeer(MovimientoBultoRepartoDTO peticion, string usuario)
        {
            CodigoBultoFurgoneta codigo = CodigoBultoFurgoneta.Leer(peticion.Codigo);
            if (codigo == null)
            {
                return MalMovimiento(TipoResultadoReparto.Invalido,
                    $"«{peticion.Codigo?.Trim()}» no es un bulto de la furgoneta: para quitarlo sin leer, elija un bulto de la lista de su furgoneta (GET api/RepartoPropio/Furgoneta).");
            }
            MovimientoBultoReparto previa = await paradas.Furgoneta.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                if (previa.Codigo != codigo.Codigo || previa.Tipo != TiposMovimientoBulto.DESCARGADO)
                {
                    return MalMovimiento(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para el bulto {previa.Codigo}. Cada lectura lleva su propia clave.");
                }
                return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaSinLeer(previa, usuario, null).ConfigureAwait(false));
            }
            List<MovimientoBultoReparto> historia = await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Codigos = new[] { codigo.Codigo } })
                .ConfigureAwait(false) ?? new List<MovimientoBultoReparto>();
            MovimientoBultoReparto ultimo = NucleoFurgoneta.Ultimo(historia, codigo.Codigo);
            if (ultimo != null && ultimo.Tipo == TiposMovimientoBulto.DESCARGADO)
            {
                ResultadoMovimientoBultoDTO ya = await RespuestaDescarga(ultimo, usuario, null, true).ConfigureAwait(false);
                ya.SinLeer = true;
                return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(ya);
            }
            if (ultimo == null || !TiposMovimientoBulto.DejaEnFurgoneta(ultimo.Tipo))
            {
                return NoEnFurgoneta(codigo.Codigo, ultimo);
            }
            if (!MismoUsuario(ultimo.Usuario, usuario))
            {
                return new ResultadoReparto<ResultadoMovimientoBultoDTO>
                {
                    Tipo = TipoResultadoReparto.Conflicto,
                    Mensaje = $"El bulto {codigo.Codigo} va en la furgoneta de {NombreRepartidor(ultimo.Usuario)}: sin leerlo solo puede quitar los de la suya. " +
                        "Si lo tiene usted, léalo con «Descargar».",
                    Codigo = CODIGO_EN_OTRA_FURGONETA,
                    Detalles = new { codigo = codigo.Codigo, repartidor = NombreRepartidor(ultimo.Usuario) }
                };
            }
            MovimientoBultoReparto nuevo = Descargado(ultimo, peticion, usuario, ahora(), peticion.ClaveIdempotencia);
            int insertado = await paradas.Furgoneta.RegistrarConRastro(nuevo, TABLA_MODIFICACIONES_SIN_LEER,
                JsonConvert.SerializeObject(ultimo), JsonConvert.SerializeObject(new { SinLeer = true, Movimiento = nuevo }), usuario.Trim()).ConfigureAwait(false);
            List<string> avisados = null;
            if (insertado > 0 && paradas.AvisosFurgoneta != null)
            {
                try
                {
                    avisados = await paradas.AvisosFurgoneta.BultoQuitadoSinLeer(usuario, nuevo).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Furgoneta] No se pudo avisar a Almacén del bulto {nuevo.Codigo} quitado sin leer por {NombreRepartidor(usuario)}: {ex.Message}", ex));
                }
            }
            return ResultadoReparto<ResultadoMovimientoBultoDTO>.Bien(await RespuestaSinLeer(nuevo, usuario, avisados).ConfigureAwait(false));
        }

        private async Task<ResultadoMovimientoBultoDTO> RespuestaSinLeer(MovimientoBultoReparto m, string usuario, List<string> avisados)
        {
            ResultadoMovimientoBultoDTO dto = await Respuesta(m, usuario, false).ConfigureAwait(false);
            dto.SinLeer = true;
            dto.Descargados = new List<string> { m.Codigo };
            dto.Mensaje = $"Bulto {m.Codigo} quitado de su furgoneta sin leerlo." +
                (avisados != null && avisados.Count > 0 ? $" Se ha avisado a {string.Join(", ", avisados)}." : string.Empty);
            return dto;
        }

        private async Task<ResultadoMovimientoBultoDTO> RespuestaDescarga(MovimientoBultoReparto m, string usuario, string loLlevaba, bool yaEstaba)
        {
            ResultadoMovimientoBultoDTO dto = await Respuesta(m, usuario, yaEstaba).ConfigureAwait(false);
            dto.Descargados = yaEstaba ? new List<string>() : new List<string> { m.Codigo };
            dto.Mensaje = yaEstaba
                ? $"El bulto {m.Codigo} ya estaba descargado."
                : $"Bulto {m.Codigo} descargado" + (loLlevaba != null && !MismoUsuario(loLlevaba, usuario) ? $" (lo llevaba {NombreRepartidor(loLlevaba)})." : ".");
            return dto;
        }

        private async Task<ResultadoMovimientoBultoDTO> Respuesta(MovimientoBultoReparto m, string usuario, bool yaEstaba)
            => new ResultadoMovimientoBultoDTO
            {
                Codigo = m.Codigo,
                TipoCodigo = m.TipoCodigo,
                Tipo = m.Tipo,
                Bulto = m.Bulto,
                Total = m.Total,
                EnvioId = m.EnvioId,
                Pedido = m.Pedido,
                Traspaso = m.NumTraspaso,
                Descripcion = m.Descripcion,
                YaEstaba = yaEstaba,
                EnFurgoneta = (await EnFurgoneta(usuario).ConfigureAwait(false)).Count
            };

        // ---------------------------------------------------------------- terminar jornada

        /// <summary>
        /// POST api/RepartoPropio/Jornada/Terminar: con la furgoneta vacía, termina. Con bultos y sin <c>Confirmar</c>, devuelve la
        /// lista (Terminada = false) y no guarda nada. Con <c>Confirmar</c>, queda en RepartoJornadas y avisa en la campana de Nesto a
        /// Almacén, Dirección y al repartidor. Idempotente por la clave (no avisa dos veces).
        /// </summary>
        public async Task<ResultadoReparto<ResultadoJornadaRepartoDTO>> TerminarJornada(TerminarJornadaRepartoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            if (!FurgonetaDisponible())
            {
                return ResultadoReparto<ResultadoJornadaRepartoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_FURGONETA);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return ResultadoReparto<ResultadoJornadaRepartoDTO>.Mal(TipoResultadoReparto.Invalido, "Falta la ClaveIdempotencia de la jornada.");
            }
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return ResultadoReparto<ResultadoJornadaRepartoDTO>.Mal(TipoResultadoReparto.Invalido, "No se sabe quién termina la jornada.");
            }
            JornadaReparto previa = await paradas.Furgoneta.LeerJornadaPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                return ResultadoReparto<ResultadoJornadaRepartoDTO>.Bien(JornadaYaTerminada(previa));
            }
            // NestoAPI#844: con dinero en la caja no se termina (ni confirmando): primero, cerrar la caja. No se guarda nada.
            if (ControlCaja != null && await ControlCaja.MotivoParaNoTerminar(usuario).ConfigureAwait(false) is string cajaSinCerrar)
            {
                return ResultadoReparto<ResultadoJornadaRepartoDTO>.Bien(new ResultadoJornadaRepartoDTO
                {
                    Terminada = false,
                    CerrarCaja = true,
                    Mensaje = cajaSinCerrar
                });
            }
            List<MovimientoBultoReparto> pendientes = await EnFurgoneta(usuario).ConfigureAwait(false);
            List<BultoFurgonetaDTO> lista = pendientes.Select(NucleoFurgoneta.ADto).ToList();
            if (pendientes.Count > 0 && !peticion.Confirmar)
            {
                return ResultadoReparto<ResultadoJornadaRepartoDTO>.Bien(new ResultadoJornadaRepartoDTO
                {
                    Terminada = false,
                    Pendientes = lista,
                    Mensaje = $"Quedan {TextoBultos(pendientes.Count)} en la furgoneta. Descárguelos o confirme que termina igualmente: se avisará a Almacén y Dirección."
                });
            }
            DateTime momento = ahora();
            DateTime fin = peticion.FechaHora ?? momento;
            var jornada = new JornadaReparto
            {
                Usuario = usuario.Trim(),
                Fecha = fin.Date,
                FechaFin = fin,
                BultosPendientes = pendientes.Count,
                Pendientes = pendientes.Count == 0 ? null : string.Join(",", pendientes.Select(p => p.Codigo)),
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                FechaRegistro = momento
            };
            try
            {
                await paradas.Furgoneta.RegistrarJornada(jornada).ConfigureAwait(false);
            }
            catch (ClaveIdempotenciaRepetidaException)
            {
                JornadaReparto ganadora = await paradas.Furgoneta.LeerJornadaPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
                return ganadora != null
                    ? ResultadoReparto<ResultadoJornadaRepartoDTO>.Bien(JornadaYaTerminada(ganadora))
                    : ResultadoReparto<ResultadoJornadaRepartoDTO>.Mal(TipoResultadoReparto.Conflicto, "Esa jornada ya se está terminando. Vuelva a intentarlo.");
            }
            var avisados = new List<string>();
            if (pendientes.Count > 0 && paradas.AvisosFurgoneta != null)
            {
                try
                {
                    avisados = await paradas.AvisosFurgoneta.FurgonetaSinDescargar(usuario, pendientes, true).ConfigureAwait(false) ?? new List<string>();
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo avisar de la furgoneta sin descargar de {NombreRepartidor(usuario)}: {ex.Message}", ex));
                }
            }
            Avisar(TipoResultadoReparto.Ok, usuario, RepartoCambiadoDTO.MOTIVO_FURGONETA, origen);
            return ResultadoReparto<ResultadoJornadaRepartoDTO>.Bien(new ResultadoJornadaRepartoDTO
            {
                Terminada = true,
                Pendientes = lista,
                Avisados = avisados,
                Mensaje = pendientes.Count == 0
                    ? "Jornada terminada: la furgoneta está vacía."
                    : $"Jornada terminada con {TextoBultos(pendientes.Count)} en la furgoneta." +
                      (avisados.Count > 0 ? $" Se ha avisado a {string.Join(", ", avisados)}." : string.Empty)
            });
        }

        private static ResultadoJornadaRepartoDTO JornadaYaTerminada(JornadaReparto jornada)
            => new ResultadoJornadaRepartoDTO
            {
                Terminada = true,
                Pendientes = (jornada.Pendientes ?? string.Empty).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(c => new BultoFurgonetaDTO { Codigo = c.Trim(), Usuario = NombreRepartidor(jornada.Usuario) }).ToList(),
                Mensaje = $"La jornada ya se terminó a las {jornada.FechaFin:H:mm}" +
                    (jornada.BultosPendientes > 0 ? $" con {TextoBultos(jornada.BultosPendientes)} en la furgoneta." : ".")
            };

        internal static string TextoBultos(int n) => n == 1 ? "1 bulto" : $"{n} bultos";

        // ---------------------------------------------------------------- cargar y entregar (los ganchos)

        /// <summary>
        /// Después de un «Cargar» que ha ido bien: el movimiento Cargado del bulto (con la misma clave), salvo que ya lo lleve él. Las
        /// NR no: al cargarlas por la mañana todavía no hay bulto (la etiqueta se pega al recoger). Incidencia 1189 (11/11/26): nada
        /// que cargue la parada de una RECOGIDA (ni su RP ni su NE: lo que se lea) ni, NestoAPI#786, de una nota de COBRO: no hay
        /// bulto. La carga de la parada (quién la lleva, BultosCargados, soloMios) sigue en EnviosAgenciaCargaBultos. Nunca rompe.
        /// </summary>
        private async Task AnotarCarga(CargaBultoRepartoDTO peticion, ResultadoCargaBultoDTO cargado, string usuario)
        {
            if (cargado == null || !FurgonetaDisponible())
            {
                return;
            }
            try
            {
                MovimientoBultoReparto nuevo;
                if (EsCodigoReposicion(peticion.Codigo, out int traspaso, out int bultoReposicion))
                {
                    FilaBultoReposicion etiqueta = paradas.BultosReposicion == null ? null
                        : await paradas.BultosReposicion.LeerPorCodigo(Constantes.Empresas.EMPRESA_POR_DEFECTO, CodigosBultoReposicion.Codigo(traspaso, bultoReposicion)).ConfigureAwait(false);
                    nuevo = new MovimientoBultoReparto
                    {
                        Codigo = CodigosBultoReposicion.Codigo(traspaso, bultoReposicion),
                        TipoCodigo = CodigoBultoFurgoneta.R,
                        EnvioId = cargado.EnvioId,
                        NumTraspaso = traspaso,
                        Bulto = bultoReposicion,
                        Total = etiqueta == null ? (int?)null : Math.Max((int)etiqueta.Total, bultoReposicion),
                        Descripcion = $"Reposición {traspaso} Algete → {cargado.Nombre?.Replace("Reposición ", string.Empty)}"
                    };
                }
                else
                {
                    CodigoBultoFurgoneta codigo = CodigoBultoFurgoneta.Leer(peticion.Codigo);
                    if (codigo?.TipoCodigo == CodigoBultoFurgoneta.NR)
                    {
                        return;
                    }
                    NotaParadaReparto nota = cargado.EnvioId > 0 ? await NotaDeEnvio(cargado.EnvioId).ConfigureAwait(false) : null;
                    if (nota?.EsRecogida == true || nota?.EsCobro == true)
                    {
                        return;
                    }
                    nuevo = new MovimientoBultoReparto
                    {
                        Codigo = CodigoBultoFurgoneta.CodigoRp(cargado.EnvioId, cargado.Bulto),
                        TipoCodigo = CodigoBultoFurgoneta.RP,
                        EnvioId = cargado.EnvioId,
                        Bulto = cargado.Bulto,
                        Total = cargado.Bultos,
                        Descripcion = Descripcion(cargado.Nombre, cargado.Poblacion)
                    };
                }
                List<MovimientoBultoReparto> historia = await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Codigos = new[] { nuevo.Codigo } })
                    .ConfigureAwait(false) ?? new List<MovimientoBultoReparto>();
                if (NucleoFurgoneta.LoLleva(NucleoFurgoneta.Ultimo(historia, nuevo.Codigo), usuario))
                {
                    return;
                }
                DateTime momento = ahora();
                nuevo.Tipo = TiposMovimientoBulto.CARGADO;
                nuevo.Usuario = usuario.Trim();
                nuevo.FechaHora = peticion.FechaHora ?? momento;
                nuevo.FechaRegistro = momento;
                nuevo.Origen = OrigenesMovimientoBulto.REPARTIDOR;
                nuevo.ClaveIdempotencia = peticion.ClaveIdempotencia;
                _ = await paradas.Furgoneta.Registrar(new[] { nuevo }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] La carga de {peticion?.Codigo} está hecha, pero no se pudo apuntar en la furgoneta: {ex.Message}", ex));
            }
        }

        internal static string Descripcion(string nombre, string poblacion)
        {
            string n = nombre?.Trim();
            string p = poblacion?.Trim();
            return string.IsNullOrEmpty(p) ? n : string.IsNullOrEmpty(n) ? p : $"{n} ({p})";
        }

        /// <summary>
        /// Antes de entregar: los bultos leídos tienen que ser de esa parada; con el interruptor encendido, hay que leer al menos uno.
        /// Null si se puede seguir. Sin la tabla de la furgoneta no se mira nada (como antes). En las recogidas no se mira.
        /// </summary>
        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> ValidarBultosEntrega(int envioId, EntregaRepartoDTO peticion)
        {
            if (peticion == null || !FurgonetaDisponible())
            {
                return null;
            }
            // Ni las recogidas ni, NestoAPI#786, las notas de cobro (no tienen bultos: no se leen aunque el escaneo sea obligatorio).
            NotaParadaReparto nota = envioId > 0 ? await NotaDeEnvio(envioId).ConfigureAwait(false) : null;
            if (nota?.EsRecogida == true || nota?.EsCobro == true)
            {
                return null;
            }
            List<string> leidos = (peticion.BultosEntregados ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
            if (leidos.Count == 0)
            {
                return EscaneoEntregaObligatorio() ? Invalido(MENSAJE_ESCANEO_OBLIGATORIO) : null;
            }
            HashSet<int> traspasos = null;
            EnviosAgencia envio = null;
            foreach (string leido in leidos)
            {
                CodigoBultoFurgoneta codigo = CodigoDeEntrega(leido, envioId);
                bool esDeLaParada;
                if (codigo == null)
                {
                    esDeLaParada = false;
                }
                else if (NucleoParadasReparto.EsReposicion(envioId))
                {
                    if (traspasos == null)
                    {
                        traspasos = new HashSet<int>((await LeerCargasReposicion(new[] { NucleoParadasReparto.IdReposicion(envioId) }).ConfigureAwait(false)).Select(c => c.Traspaso));
                    }
                    esDeLaParada = codigo.TipoCodigo == CodigoBultoFurgoneta.R && traspasos.Contains(codigo.Traspaso.Value);
                }
                else
                {
                    envio = envio ?? await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
                    esDeLaParada = codigo.TipoCodigo == CodigoBultoFurgoneta.RP && codigo.EnvioId == envioId
                        && (envio == null || codigo.Bulto <= Math.Max((int)envio.Bultos, 1));
                }
                if (!esDeLaParada)
                {
                    return Invalido($"El bulto «{leido.Trim()}» no es de esta parada: quítelo de los leídos.");
                }
            }
            return null;
        }

        /// <summary>Lo leído al entregar: RP/R tal cual; NE{pedido}, el bulto 1 de su envío (RP{envío}-1).</summary>
        private static CodigoBultoFurgoneta CodigoDeEntrega(string leido, int envioId)
        {
            CodigoBultoFurgoneta codigo = CodigoBultoFurgoneta.Leer(leido);
            if (codigo == null && envioId > 0 && CodigoNotaEntrega.TryLeer(leido, out string prefijo, out _, out _) && prefijo == CodigoNotaEntrega.PREFIJO_ENTREGA)
            {
                codigo = new CodigoBultoFurgoneta { Codigo = CodigoBultoFurgoneta.CodigoRp(envioId, 1), TipoCodigo = CodigoBultoFurgoneta.RP, EnvioId = envioId, Bulto = 1 };
            }
            return codigo;
        }

        private bool EscaneoEntregaObligatorio()
        {
            try
            {
                string valor = parametros.LeerParametro(Constantes.Empresas.EMPRESA_POR_DEFECTO, Constantes.ParametrosUsuario.USUARIO_POR_DEFECTO, PARAMETRO_ESCANEO_ENTREGA_OBLIGATORIO);
                return string.Equals(valor?.Trim(), "1", StringComparison.Ordinal) || string.Equals(valor?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// Después de una entrega que ha ido bien: Entregado los leídos o, sin leer ninguno, los de esa parada que llevaba encima. En
        /// una nota de RECOGIDA no sale nada: si al recoger no se leyó ninguna NR, se apuntan Recogido NR{pedido}-1..BultosRecogidos
        /// (para que se sepa que vienen en la furgoneta). Las claves salen de la de la entrega (un reintento no duplica). Nunca rompe.
        /// </summary>
        private async Task AnotarEntrega(int envioId, EntregaRepartoDTO peticion, string usuario, EstadoEnvioRepartoDTO estado)
        {
            if (peticion == null || !FurgonetaDisponible())
            {
                return;
            }
            try
            {
                DateTime momento = ahora();
                DateTime fechaHora = peticion.FechaHora ?? momento;
                if (envioId > 0)
                {
                    EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
                    NotaParadaReparto nota = await NotaDe(envio?.Pedido).ConfigureAwait(false);
                    if (nota?.EsRecogida == true)
                    {
                        await AnotarRecogidaSinLeer(envio, nota.Pedido, peticion, usuario, fechaHora, momento).ConfigureAwait(false);
                        return;
                    }
                    if (nota?.EsCobro == true)
                    {
                        return; // NestoAPI#786: una nota de cobro no lleva nada en la furgoneta
                    }
                }
                List<MovimientoBultoReparto> deLaParada = NucleoFurgoneta.Ultimos(await paradas.Furgoneta.LeerHistoria(
                    new ConsultaMovimientosBulto { Envios = new[] { envioId } }).ConfigureAwait(false));
                List<string> leidos = (peticion.BultosEntregados ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
                List<MovimientoBultoReparto> aEntregar;
                if (leidos.Count == 0)
                {
                    aEntregar = deLaParada.Where(m => NucleoFurgoneta.LoLleva(m, usuario)).ToList();
                }
                else
                {
                    aEntregar = new List<MovimientoBultoReparto>();
                    foreach (CodigoBultoFurgoneta codigo in leidos.Select(l => CodigoDeEntrega(l, envioId)).Where(c => c != null)
                        .GroupBy(c => c.Codigo).Select(g => g.First()))
                    {
                        MovimientoBultoReparto ultimo = deLaParada.FirstOrDefault(m => m.Codigo == codigo.Codigo);
                        if (ultimo == null)
                        {
                            List<MovimientoBultoReparto> historia = await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Codigos = new[] { codigo.Codigo } })
                                .ConfigureAwait(false);
                            ultimo = NucleoFurgoneta.Ultimo(historia, codigo.Codigo);
                        }
                        if (ultimo?.Tipo == TiposMovimientoBulto.ENTREGADO)
                        {
                            continue;
                        }
                        aEntregar.Add(ultimo ?? new MovimientoBultoReparto
                        {
                            Codigo = codigo.Codigo,
                            TipoCodigo = codigo.TipoCodigo,
                            EnvioId = envioId,
                            NumTraspaso = codigo.Traspaso,
                            Bulto = codigo.Bulto
                        });
                    }
                }
                List<MovimientoBultoReparto> nuevos = aEntregar
                    .Select(m => Copia(m, TiposMovimientoBulto.ENTREGADO, usuario.Trim(), fechaHora, momento, OrigenesMovimientoBulto.REPARTIDOR,
                        NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, m.Codigo, TiposMovimientoBulto.ENTREGADO)))
                    .ToList();
                foreach (MovimientoBultoReparto nuevo in nuevos)
                {
                    nuevo.EnvioId = nuevo.EnvioId ?? envioId;
                    nuevo.Latitud = peticion.Latitud;
                    nuevo.Longitud = peticion.Longitud;
                }
                _ = await paradas.Furgoneta.Registrar(nuevos).ConfigureAwait(false);
                if (estado != null)
                {
                    estado.BultosEntregados = nuevos.Count;
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] La entrega de la parada {envioId} está hecha, pero no se pudo apuntar en la furgoneta: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Incidencia 1190 (11/11/26): una incidencia de un envío cuyos bultos no están en NINGUNA furgoneta (salió sin leerlo al
        /// cargar, o se descargó y volvió a salir) deja apuntado el Cargado de sus bultos a nombre del que la pone, con la hora de la
        /// incidencia: el paquete sigue con él, y así sale en GET Furgoneta y se puede descargar (con su RP, con el papel NE o sin
        /// leer). Si algún bulto va en alguna furgoneta, no se toca nada (ya se sabe quién lo lleva). Ni las recogidas ni las notas
        /// de cobro (no tienen bulto). Claves derivadas de la de la incidencia: un reintento no duplica. Nunca rompe la incidencia.
        /// </summary>
        private async Task AnotarSalidaSinCargar(int envioId, IncidenciaRepartoDTO peticion, string usuario)
        {
            if (peticion == null || envioId <= 0 || string.IsNullOrWhiteSpace(usuario) || !FurgonetaDisponible())
            {
                return;
            }
            try
            {
                EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
                if (envio == null || envio.Agencia != Constantes.Agencias.AGENCIA_REPARTO_PROPIO)
                {
                    return;
                }
                NotaParadaReparto nota = await NotaDe(envio.Pedido).ConfigureAwait(false);
                if (nota?.EsRecogida == true || nota?.EsCobro == true)
                {
                    return;
                }
                List<MovimientoBultoReparto> ultimos = NucleoFurgoneta.Ultimos(await paradas.Furgoneta.LeerHistoria(
                    new ConsultaMovimientosBulto { Envios = new[] { envioId } }).ConfigureAwait(false))
                    .Where(m => m.TipoCodigo == CodigoBultoFurgoneta.RP).ToList();
                if (ultimos.Any(m => TiposMovimientoBulto.DejaEnFurgoneta(m.Tipo)))
                {
                    return;
                }
                int bultos = Math.Max((int)envio.Bultos, 1);
                DateTime momento = ahora();
                List<MovimientoBultoReparto> nuevos = Enumerable.Range(1, Math.Min(bultos, Models.PedidosVenta.TiposNotaEntrega.BULTOS_MAXIMO))
                    .Select(n => CodigoBultoFurgoneta.CodigoRp(envioId, n))
                    .Where(c => ultimos.All(u => !string.Equals(u.Codigo, c, StringComparison.OrdinalIgnoreCase) || u.Tipo != TiposMovimientoBulto.ENTREGADO))
                    .Select(c => new MovimientoBultoReparto
                    {
                        Codigo = c,
                        TipoCodigo = CodigoBultoFurgoneta.RP,
                        EnvioId = envioId,
                        Bulto = CodigoBultoFurgoneta.Leer(c).Bulto,
                        Total = bultos,
                        Descripcion = Descripcion(envio.Nombre, envio.Poblacion),
                        Tipo = TiposMovimientoBulto.CARGADO,
                        Usuario = usuario.Trim(),
                        FechaHora = peticion.FechaHora ?? momento,
                        FechaRegistro = momento,
                        Latitud = peticion.Latitud,
                        Longitud = peticion.Longitud,
                        Origen = OrigenesMovimientoBulto.REPARTIDOR,
                        ClaveIdempotencia = NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, c, TiposMovimientoBulto.CARGADO)
                    })
                    .ToList();
                _ = await paradas.Furgoneta.Registrar(nuevos).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta] La incidencia del envío {envioId} está hecha, pero no se pudo apuntar en la furgoneta: {ex.Message}", ex));
            }
        }

        private async Task AnotarRecogidaSinLeer(EnviosAgencia envio, int pedido, EntregaRepartoDTO peticion, string usuario, DateTime fechaHora, DateTime momento)
        {
            int bultos = peticion.BultosRecogidos ?? 0;
            if (bultos <= 0)
            {
                return;
            }
            bool algunaLeida = (await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Pedidos = new[] { pedido } }).ConfigureAwait(false)
                ?? new List<MovimientoBultoReparto>())
                .Any(m => m.TipoCodigo == CodigoBultoFurgoneta.NR && m.Tipo == TiposMovimientoBulto.RECOGIDO);
            if (algunaLeida)
            {
                return;
            }
            var nuevos = Enumerable.Range(1, Math.Min(bultos, Models.PedidosVenta.TiposNotaEntrega.BULTOS_MAXIMO)).Select(n =>
            {
                string codigo = CodigoNotaEntrega.BultoRecogida(pedido, n);
                return new MovimientoBultoReparto
                {
                    Codigo = codigo,
                    TipoCodigo = CodigoBultoFurgoneta.NR,
                    EnvioId = envio?.Numero,
                    Pedido = pedido,
                    Bulto = n,
                    Total = bultos,
                    Tipo = TiposMovimientoBulto.RECOGIDO,
                    Usuario = usuario.Trim(),
                    FechaHora = fechaHora,
                    FechaRegistro = momento,
                    Latitud = peticion.Latitud,
                    Longitud = peticion.Longitud,
                    Origen = OrigenesMovimientoBulto.REPARTIDOR,
                    Descripcion = $"Recogida {pedido}" + (string.IsNullOrWhiteSpace(envio?.Nombre) ? string.Empty : $": {envio.Nombre.Trim()}"),
                    ClaveIdempotencia = NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, codigo, TiposMovimientoBulto.RECOGIDO)
                };
            }).ToList();
            _ = await paradas.Furgoneta.Registrar(nuevos).ConfigureAwait(false);
        }

        // ---------------------------------------------------------------- piezas

        internal static MovimientoBultoReparto Copia(MovimientoBultoReparto de, string tipo, string usuario, DateTime fechaHora, DateTime fechaRegistro,
            string origen, Guid clave)
            => new MovimientoBultoReparto
            {
                Codigo = de.Codigo,
                TipoCodigo = de.TipoCodigo,
                EnvioId = de.EnvioId,
                NumTraspaso = de.NumTraspaso,
                Pedido = de.Pedido,
                Bulto = de.Bulto,
                Total = de.Total,
                Descripcion = de.Descripcion,
                Tipo = tipo,
                Usuario = usuario,
                FechaHora = fechaHora,
                FechaRegistro = fechaRegistro,
                Origen = origen,
                ClaveIdempotencia = clave
            };

        private ResultadoReparto<ResultadoMovimientoBultoDTO> ValidarMovimiento(MovimientoBultoRepartoDTO peticion, string usuario, string que)
        {
            if (!FurgonetaDisponible())
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_FURGONETA);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, $"Falta la ClaveIdempotencia de {que}.");
            }
            if (string.IsNullOrWhiteSpace(peticion.Codigo))
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, "Falta el código del bulto.");
            }
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, "No se sabe quién lee el bulto.");
            }
            return null;
        }

        private static ResultadoReparto<ResultadoMovimientoBultoDTO> MalMovimiento(TipoResultadoReparto tipo, string mensaje)
            => ResultadoReparto<ResultadoMovimientoBultoDTO>.Mal(tipo, mensaje);

        /// <summary>
        /// AlmacenHub, grupo «Reposiciones:{tienda}» (el de «Enviar reposición» de Nesto): «haCambiado». Nesto relee el estado de la
        /// recogida. Nunca rompe la respuesta.
        /// </summary>
        private void AvisarReposiciones(string tienda, string usuario, OrigenPeticionReparto origen)
        {
            if (paradas.AvisosAlmacen == null || string.IsNullOrWhiteSpace(tienda))
            {
                return;
            }
            try
            {
                paradas.AvisosAlmacen.HaCambiado(Notificaciones.AvisosAlmacen.GrupoReposiciones(tienda), usuario, origen?.Conexion, origen?.Dispositivo);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Furgoneta #772] No se pudo avisar a las reposiciones de {tienda}: {ex.Message}", ex));
            }
        }
    }

    /// <summary>NestoAPI#772: dejar «Recogida» la reposición de hoy de una tienda (lo mismo que POST api/Reposiciones/Recogida).</summary>
    public interface IRecogidaReposicionesReparto
    {
        /// <summary>True si ha quedado marcada ahora. False si no se podía (ya marcada, sin viaje hoy…): no es un error.</summary>
        Task<bool> Marcar(string empresa, string tienda, IPrincipal quien);
    }

    /// <summary>
    /// NestoAPI#772: <see cref="Reposiciones.ServicioPreparacionReposicion.MarcarRecogida"/>, con el usuario del repartidor (Almacén puede
    /// marcarla: sus permisos son los de la tienda «o Almacén y Dirección»). Lo que no se puede marcar (ya marcada, sin viaje…) no es
    /// un error: la reposición sigue y el estado lo dice.
    /// </summary>
    public class RecogidaReposicionesReparto : IRecogidaReposicionesReparto
    {
        private readonly Func<Reposiciones.IServicioPreparacionReposicion> preparacion;

        public RecogidaReposicionesReparto(NVEntities db) : this(() => new Reposiciones.ServicioPreparacionReposicion(db))
        {
        }

        internal RecogidaReposicionesReparto(Func<Reposiciones.IServicioPreparacionReposicion> preparacion)
        {
            this.preparacion = preparacion;
        }

        public async Task<bool> Marcar(string empresa, string tienda, IPrincipal quien)
        {
            try
            {
                Reposiciones.EstadoReposicionPuntualDTO estado = await preparacion().MarcarRecogida(new Reposiciones.MarcarRecogidaReposicionDTO
                {
                    Empresa = empresa,
                    Origen = tienda,
                    Destino = Constantes.Almacenes.ALGETE
                }, quien).ConfigureAwait(false);
                return estado?.FechaRecogida.HasValue == true;
            }
            catch (Exceptions.NestoBusinessException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }
}
