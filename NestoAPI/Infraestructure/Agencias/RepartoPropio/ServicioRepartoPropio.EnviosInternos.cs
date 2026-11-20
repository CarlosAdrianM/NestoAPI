using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// Sugerencia 1277: los envíos internos entre sedes en el reparto, con el patrón de las paradas de reposición (parada con EnvioId
    /// negativo y la sede como dirección) y las reglas de siempre: la furgoneta por movimientos (<see cref="NucleoFurgoneta"/>), «solo lo
    /// cargado» (<see cref="ReglaCargaObligatoria"/>) y la entrega incompleta (<see cref="EntregaIncompletaReparto.EvaluarCodigos"/>).
    /// <list type="bullet">
    /// <item>Cargar EI (origen Algete o bulto ya descargado en el almacén): su Cargado es la carga.</item>
    /// <item>Recoger EI (origen tienda): su Recogido, en la parada de recogida del origen.</item>
    /// <item>Entregar la parada de entrega: nombre de quien lo recibe (obligatorio), sin cobro, firma ni DNI; BultosEntregados opcional.</item>
    /// <item>Descargar: el de siempre (el código EI se entiende como los demás).</item>
    /// </list>
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        private bool EnviosInternosDisponibles()
        {
            try
            {
                return paradas.EnviosInternos != null && paradas.EnviosInternos.ScriptAplicado();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private async Task<List<MovimientoBultoReparto>> HistoriaEnviosInternos(IEnumerable<int> ids)
        {
            List<int> envios = ids.Distinct().Select(ReglasEnviosInternos.EnvioIdEntrega).ToList();
            if (envios.Count == 0 || !FurgonetaDisponible())
            {
                return new List<MovimientoBultoReparto>();
            }
            return await paradas.Furgoneta.LeerHistoria(new ConsultaMovimientosBulto { Envios = envios }).ConfigureAwait(false)
                ?? new List<MovimientoBultoReparto>();
        }

        private static EnvioRepartoDTO DtoEnvioInterno(EnvioInternoReparto e, ParadaEnvioInterno parada, EnvioRepartoDTO dto)
        {
            string sede = parada.Sede;
            dto.EnvioId = parada.EnvioId;
            dto.Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            dto.TipoParada = TiposParadaReparto.ENVIO_INTERNO;
            dto.CodigoDocumento = ReglasEnviosInternos.Documento(e.Id);
            dto.FaseEnvioInterno = parada.EsRecogida ? FasesEnvioInterno.RECOGIDA : FasesEnvioInterno.ENTREGA;
            dto.SedeOrigen = ReglasEnviosInternos.Sede(e.Origen);
            dto.SedeDestino = ReglasEnviosInternos.Sede(e.Destino);
            dto.SedeParada = sede;
            dto.DescripcionEnvioInterno = e.Descripcion?.Trim();
            dto.Nombre = (parada.EsRecogida ? "Recoger envío interno: " : "Envío interno: ") + NombreTienda(e.Origen) + " → " + NombreTienda(e.Destino);
            dto.TextoNota = e.Descripcion?.Trim();
            dto.Bultos = parada.Bultos;
            dto.BultosCargados = parada.BultosCargados;
            dto.Repartidor = NombreRepartidor(parada.Repartidor);
            dto.Reembolso = 0m;
            dto.Estado = EnvioRepartoDTO.ESTADO_TRAMITADO;
            dto.FechaParada = e.FechaAlta;
            PedidosVenta.TiendasRecogida.Tienda tienda = PedidosVenta.TiendasRecogida.Buscar(sede);
            dto.Poblacion = tienda?.Poblacion ?? NombreTienda(sede);
            if (tienda != null)
            {
                dto.Direccion = tienda.Direccion;
                dto.CodigoPostal = tienda.CodigoPostal;
                dto.Telefono = tienda.Telefono;
            }
            CoordenadasGeo geo = SedesReparto.DeAlmacen(sede);
            dto.Latitud = geo?.Latitud;
            dto.Longitud = geo?.Longitud;
            return dto;
        }

        /// <summary>Las paradas de los envíos internos vivos de los últimos 30 días. Sin el script, ninguna (como antes).</summary>
        private async Task<List<EnvioRepartoDTO>> ParadasEnviosInternos(DateTime dia)
        {
            if (!EnviosInternosDisponibles())
            {
                return new List<EnvioRepartoDTO>();
            }
            try
            {
                List<EnvioInternoReparto> vivos = (await paradas.EnviosInternos.LeerVivos(dia.AddDays(-DIAS_ATRAS_LISTA)).ConfigureAwait(false)
                    ?? new List<EnvioInternoReparto>()).Where(e => e.FechaAlta.Date <= dia.Date).ToList();
                List<MovimientoBultoReparto> historia = await HistoriaEnviosInternos(vivos.Select(e => e.Id)).ConfigureAwait(false);
                return vivos.SelectMany(e => ReglasEnviosInternos.Paradas(e, historia).Select(p => DtoEnvioInterno(e, p, new EnvioRepartoDTO()))).ToList();
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Envíos internos] No se han podido leer las paradas de los envíos internos: {ex.Message}", ex));
                return new List<EnvioRepartoDTO>();
            }
        }

        /// <summary>Los dueños de las paradas de envíos internos pedidas (para el orden): las vivas, por quien lleva sus bultos.</summary>
        private async Task<Dictionary<int, string>> DuenosParadasEnviosInternos(IReadOnlyCollection<int> ids)
        {
            var duenos = new Dictionary<int, string>();
            List<int> internos = ids.Where(ReglasEnviosInternos.EsParadaEnvioInterno).Select(ReglasEnviosInternos.IdDeParada).Distinct().ToList();
            if (internos.Count == 0 || !EnviosInternosDisponibles())
            {
                return duenos;
            }
            List<MovimientoBultoReparto> historia = await HistoriaEnviosInternos(internos).ConfigureAwait(false);
            foreach (int id in internos)
            {
                EnvioInternoReparto e = await paradas.EnviosInternos.Leer(id).ConfigureAwait(false);
                foreach (ParadaEnvioInterno p in e == null ? new List<ParadaEnvioInterno>() : ReglasEnviosInternos.Paradas(e, historia))
                {
                    if (ids.Contains(p.EnvioId))
                    {
                        duenos[p.EnvioId] = p.Repartidor;
                    }
                }
            }
            return duenos;
        }

        /// <summary>GET Bulto/EI12-1: la parada de recogida si ese bulto sigue en el origen; si no, la de entrega.</summary>
        private async Task<ResultadoReparto<EnvioRepartoBultoDTO>> BultoEnvioInterno(int id, int bulto)
        {
            if (!EnviosInternosDisponibles())
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.Invalido, ReglasEnviosInternos.MENSAJE_FALTA_SCRIPT);
            }
            string codigo = ReglasEnviosInternos.Codigo(id, bulto);
            EnvioInternoReparto e = await paradas.EnviosInternos.Leer(id).ConfigureAwait(false);
            if (e == null || bulto > Math.Max(e.Bultos, 1))
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"El bulto {codigo} no es de ningún envío interno.");
            }
            if (!ReglasEnviosInternos.EsVivo(e))
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.Conflicto, e.Estado == EstadosEnvioInterno.ANULADO
                    ? $"El envío interno {ReglasEnviosInternos.Documento(id)} está anulado."
                    : $"El envío interno {ReglasEnviosInternos.Documento(id)} ya está entregado.");
            }
            List<MovimientoBultoReparto> historia = await HistoriaEnviosInternos(new[] { id }).ConfigureAwait(false);
            MovimientoBultoReparto ultimo = NucleoFurgoneta.Ultimo(historia, codigo);
            List<ParadaEnvioInterno> suyas = ReglasEnviosInternos.Paradas(e, historia);
            ParadaEnvioInterno parada = (ultimo == null ? suyas.FirstOrDefault(p => p.EsRecogida) : null) ?? suyas.First(p => !p.EsRecogida);
            var dto = (EnvioRepartoBultoDTO)DtoEnvioInterno(e, parada, new EnvioRepartoBultoDTO());
            dto.Bulto = bulto;
            await CompletarOrden(new List<EnvioRepartoDTO> { dto }).ConfigureAwait(false);
            return ResultadoReparto<EnvioRepartoBultoDTO>.Bien(dto);
        }

        /// <summary>
        /// Cargar EI: si el origen es Algete (o el bulto se descargó en el almacén), su movimiento Cargado. Si el origen es una tienda y el
        /// bulto sigue allí, se recoge con «Recoger». De otro repartidor sin Reasignar, 409 ENVIO_DE_OTRO_REPARTIDOR.
        /// </summary>
        private async Task<ResultadoReparto<ResultadoCargaBultoDTO>> CargarEnvioInterno(CargaBultoRepartoDTO peticion, int id, int bulto, string usuario)
        {
            if (!EnviosInternosDisponibles() || !FurgonetaDisponible())
            {
                return MalCarga(TipoResultadoReparto.Invalido, !EnviosInternosDisponibles() ? ReglasEnviosInternos.MENSAJE_FALTA_SCRIPT : MENSAJE_FALTA_SCRIPT_FURGONETA);
            }
            if (peticion.ClaveIdempotencia == Guid.Empty)
            {
                return MalCarga(TipoResultadoReparto.Invalido, "Falta la ClaveIdempotencia de la carga.");
            }
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return MalCarga(TipoResultadoReparto.Invalido, "No se sabe quién carga el bulto.");
            }
            string codigo = ReglasEnviosInternos.Codigo(id, bulto);
            EnvioInternoReparto e = await paradas.EnviosInternos.Leer(id).ConfigureAwait(false);
            if (e == null || bulto > Math.Max(e.Bultos, 1))
            {
                return MalCarga(TipoResultadoReparto.NoEncontrado, $"El bulto {codigo} no es de ningún envío interno.");
            }
            MovimientoBultoReparto previa = await paradas.Furgoneta.LeerPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                return string.Equals(previa.Codigo, codigo, StringComparison.OrdinalIgnoreCase) && previa.Tipo == TiposMovimientoBulto.CARGADO
                    ? await RespuestaCargaEnvioInterno(e, bulto, previa.Usuario).ConfigureAwait(false)
                    : MalCarga(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para el bulto {previa.Codigo}. Cada lectura lleva su propia clave.");
            }
            if (!ReglasEnviosInternos.EsVivo(e))
            {
                return MalCarga(TipoResultadoReparto.Conflicto, e.Estado == EstadosEnvioInterno.ANULADO
                    ? $"El envío interno {ReglasEnviosInternos.Documento(id)} está anulado: no hay que cargarlo."
                    : $"El envío interno {ReglasEnviosInternos.Documento(id)} ya está entregado: no hay que cargarlo.");
            }
            List<MovimientoBultoReparto> historia = await HistoriaEnviosInternos(new[] { id }).ConfigureAwait(false);
            MovimientoBultoReparto ultimo = NucleoFurgoneta.Ultimo(historia, codigo);
            if (ultimo == null && ReglasEnviosInternos.Sede(e.Origen) != Constantes.Almacenes.ALGETE)
            {
                return MalCarga(TipoResultadoReparto.Conflicto,
                    $"El envío interno {ReglasEnviosInternos.Documento(id)} sale de {NombreTienda(e.Origen)}: sus bultos se recogen allí con «Recoger».");
            }
            if (NucleoFurgoneta.LoLleva(ultimo, usuario))
            {
                return await RespuestaCargaEnvioInterno(e, bulto, usuario).ConfigureAwait(false);
            }
            string actual = ReglasEnviosInternos.Paradas(e, historia).FirstOrDefault(p => !p.EsRecogida)?.Repartidor;
            if (NucleoParadasReparto.EsDeOtro(actual, usuario) && !peticion.Reasignar)
            {
                return NucleoParadasReparto.ConflictoDeOtro<ResultadoCargaBultoDTO>(actual);
            }
            DateTime momento = ahora();
            _ = await paradas.Furgoneta.Registrar(new[]
            {
                MovimientoEnvioInterno(e, bulto, TiposMovimientoBulto.CARGADO, usuario, peticion.FechaHora ?? momento, momento, peticion.ClaveIdempotencia,
                    CoordenadasGeo.Validas(peticion.Latitud, peticion.Longitud) ? peticion.Latitud : null,
                    CoordenadasGeo.Validas(peticion.Latitud, peticion.Longitud) ? peticion.Longitud : null)
            }).ConfigureAwait(false);
            return await RespuestaCargaEnvioInterno(e, bulto, usuario).ConfigureAwait(false);
        }

        private static MovimientoBultoReparto MovimientoEnvioInterno(EnvioInternoReparto e, int bulto, string tipo, string usuario, DateTime fechaHora,
            DateTime momento, Guid clave, decimal? latitud = null, decimal? longitud = null)
            => new MovimientoBultoReparto
            {
                Codigo = ReglasEnviosInternos.Codigo(e.Id, bulto),
                TipoCodigo = CodigoBultoFurgoneta.EI,
                EnvioId = ReglasEnviosInternos.EnvioIdEntrega(e.Id),
                Bulto = bulto,
                Total = Math.Max(e.Bultos, 1),
                Tipo = tipo,
                Usuario = usuario?.Trim(),
                FechaHora = fechaHora,
                FechaRegistro = momento,
                Latitud = latitud,
                Longitud = longitud,
                Origen = OrigenesMovimientoBulto.REPARTIDOR,
                Descripcion = ReglasEnviosInternos.Descripcion(e),
                ClaveIdempotencia = clave
            };

        private async Task<ResultadoReparto<ResultadoCargaBultoDTO>> RespuestaCargaEnvioInterno(EnvioInternoReparto e, int bulto, string usuario)
        {
            List<MovimientoBultoReparto> historia = await HistoriaEnviosInternos(new[] { e.Id }).ConfigureAwait(false);
            ParadaEnvioInterno entrega = ReglasEnviosInternos.Paradas(e, historia).FirstOrDefault(p => !p.EsRecogida)
                ?? new ParadaEnvioInterno { EnvioId = ReglasEnviosInternos.EnvioIdEntrega(e.Id), Sede = ReglasEnviosInternos.Sede(e.Destino), Bultos = e.Bultos };
            EnvioRepartoDTO dto = DtoEnvioInterno(e, entrega, new EnvioRepartoDTO());
            return ResultadoReparto<ResultadoCargaBultoDTO>.Bien(new ResultadoCargaBultoDTO
            {
                EnvioId = dto.EnvioId,
                Bulto = bulto,
                Bultos = dto.Bultos,
                BultosCargados = dto.BultosCargados,
                Repartidor = dto.Repartidor ?? NombreRepartidor(usuario),
                Nombre = dto.Nombre,
                Poblacion = dto.Poblacion,
                TipoParada = TiposParadaReparto.ENVIO_INTERNO,
                CodigoDocumento = dto.CodigoDocumento
            });
        }

        /// <summary>Recoger EI: completa el movimiento Recogido, o el error (null si se puede recoger).</summary>
        private async Task<ResultadoReparto<ResultadoMovimientoBultoDTO>> PrepararRecogidaEnvioInterno(CodigoBultoFurgoneta codigo, MovimientoBultoReparto nuevo)
        {
            if (!EnviosInternosDisponibles())
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, ReglasEnviosInternos.MENSAJE_FALTA_SCRIPT);
            }
            int id = codigo.EnvioInterno.Value;
            EnvioInternoReparto e = await paradas.EnviosInternos.Leer(id).ConfigureAwait(false);
            if (e == null || codigo.Bulto > Math.Max(e.Bultos, 1))
            {
                return MalMovimiento(TipoResultadoReparto.NoEncontrado, $"El bulto {codigo.Codigo} no es de ningún envío interno.");
            }
            if (!ReglasEnviosInternos.EsVivo(e))
            {
                return MalMovimiento(TipoResultadoReparto.Conflicto, e.Estado == EstadosEnvioInterno.ANULADO
                    ? $"El envío interno {ReglasEnviosInternos.Documento(id)} está anulado: no hay que recogerlo."
                    : $"El envío interno {ReglasEnviosInternos.Documento(id)} ya está entregado.");
            }
            if (ReglasEnviosInternos.Sede(e.Origen) == Constantes.Almacenes.ALGETE)
            {
                return MalMovimiento(TipoResultadoReparto.Invalido, $"El bulto {codigo.Codigo} sale de Algete: se carga con «Cargar furgoneta».");
            }
            nuevo.EnvioId = ReglasEnviosInternos.EnvioIdEntrega(id);
            nuevo.Total = Math.Max(e.Bultos, 1);
            nuevo.Descripcion = ReglasEnviosInternos.Descripcion(e);
            return null;
        }

        /// <summary>
        /// Entregar la parada de entrega de un envío interno: el nombre de quien lo recibe es obligatorio; sin cobro, firma ni DNI. Los EI
        /// leídos (BultosEntregados, opcional salvo con el escaneo obligatorio) tienen que ser suyos. «Solo lo cargado» y la entrega
        /// incompleta, como en los envíos. Idempotente por la clave.
        /// </summary>
        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> EntregarEnvioInterno(int envioId, EntregaRepartoDTO peticion, string usuario,
            OrigenPeticionReparto origen)
        {
            ResultadoReparto<EstadoEnvioRepartoDTO> resultado = await EntregarEnvioInternoSinAviso(envioId, peticion, usuario).ConfigureAwait(false);
            if (resultado.Tipo == TipoResultadoReparto.Ok && resultado.Contenido?.EsNueva == true)
            {
                await ApuntarTiempoParada(envioId, new DatosTiempoParada
                {
                    Clave = peticion.ClaveIdempotencia,
                    Resultado = TiempoParadaReparto.RESULTADO_ENTREGADA,
                    Fin = peticion.FechaHora ?? ahora(),
                    Ir = peticion.FechaHoraIr,
                    Llegada = peticion.FechaHoraLlegada,
                    Latitud = peticion.Latitud,
                    Longitud = peticion.Longitud
                }, usuario).ConfigureAwait(false);
            }
            Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_ENTREGA, origen);
            return resultado;
        }

        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> EntregarEnvioInternoSinAviso(int envioId, EntregaRepartoDTO peticion, string usuario)
        {
            if (!EnviosInternosDisponibles())
            {
                return ResultadoReparto<EstadoEnvioRepartoDTO>.Mal(TipoResultadoReparto.NoDisponible, ReglasEnviosInternos.MENSAJE_FALTA_SCRIPT);
            }
            if (ReglasEnviosInternos.EsParadaRecogida(envioId))
            {
                return Invalido("En el origen, los bultos de un envío interno se leen con «Recoger»: se entregan en el destino.");
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia de la entrega.");
            }
            if (!string.IsNullOrWhiteSpace(peticion.CobroReembolso)
                && !string.Equals(peticion.CobroReembolso.Trim(), PruebaEntregaReparto.COBRO_NO_COBRADO, StringComparison.OrdinalIgnoreCase))
            {
                return Invalido("Un envío interno no lleva cobro.");
            }
            string receptor = Limpio(peticion.Receptor);
            if (receptor == null)
            {
                return Invalido("Indique el nombre de quien recibe el envío interno.");
            }
            if (receptor.Length > MAXIMO_RECEPTOR)
            {
                return Invalido($"El nombre de quien recibe no puede pasar de {MAXIMO_RECEPTOR} caracteres.");
            }
            int id = ReglasEnviosInternos.IdDeParada(envioId);
            EnvioInternoReparto e = await paradas.EnviosInternos.Leer(id).ConfigureAwait(false);
            if (e == null)
            {
                return ResultadoReparto<EstadoEnvioRepartoDTO>.Mal(TipoResultadoReparto.NoEncontrado, $"No existe el envío interno {ReglasEnviosInternos.Documento(id)}.");
            }
            var entregado = new EstadoEnvioRepartoDTO { EnvioId = envioId, Estado = EnvioRepartoDTO.ESTADO_ENTREGADO };
            if (e.Estado == EstadosEnvioInterno.ENTREGADO)
            {
                return e.ClaveEntrega == peticion.ClaveIdempotencia
                    ? ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(entregado)
                    : Conflicto($"El envío interno {ReglasEnviosInternos.Documento(id)} ya está entregado.");
            }
            if (e.Estado == EstadosEnvioInterno.ANULADO)
            {
                return Conflicto($"El envío interno {ReglasEnviosInternos.Documento(id)} está anulado.");
            }

            // Lo leído tiene que ser de esta parada
            var leidos = new List<string>();
            foreach (string leido in (peticion.BultosEntregados ?? new List<string>()).Where(c => !string.IsNullOrWhiteSpace(c)))
            {
                if (!ReglasEnviosInternos.TryLeer(leido, out int deId, out int bulto) || deId != id || bulto > Math.Max(e.Bultos, 1))
                {
                    return Invalido($"El bulto «{leido.Trim()}» no es de esta parada: quítelo de los leídos.");
                }
                string codigo = ReglasEnviosInternos.Codigo(id, bulto);
                if (!leidos.Contains(codigo))
                {
                    leidos.Add(codigo);
                }
            }
            if (leidos.Count == 0 && EscaneoEntregaObligatorio())
            {
                return Invalido(MENSAJE_ESCANEO_OBLIGATORIO);
            }

            List<MovimientoBultoReparto> historia = FurgonetaDisponible() ? await HistoriaEnviosInternos(new[] { id }).ConfigureAwait(false) : new List<MovimientoBultoReparto>();
            DateTime momento = ahora();
            DateTime fechaHora = peticion.FechaHora ?? momento;
            var movimientos = new List<MovimientoBultoReparto>();

            // «Solo lo cargado» (Reparto.CargaObligatoria): lo que no lleva quien entrega no se entrega sin leer algo suyo.
            string repartidor = ReglasEnviosInternos.Paradas(e, historia).FirstOrDefault(p => !p.EsRecogida)?.Repartidor;
            switch (ReglaCargaObligatoria.Decidir(FurgonetaDisponible() && CargaObligatoria(), true, repartidor, usuario, leidos.Count > 0))
            {
                case DecisionCargaEntrega.Bloquear:
                    return new ResultadoReparto<EstadoEnvioRepartoDTO>
                    {
                        Tipo = TipoResultadoReparto.Conflicto,
                        Mensaje = ReglaCargaObligatoria.MensajeNoCargada(repartidor),
                        Codigo = ReglaCargaObligatoria.CODIGO_NO_CARGADA,
                        Detalles = new { repartidor = NombreRepartidor(repartidor) }
                    };
                case DecisionCargaEntrega.CargarAlEntregar:
                    foreach (string codigo in leidos.Where(c => !NucleoFurgoneta.LoLleva(NucleoFurgoneta.Ultimo(historia, c), usuario)))
                    {
                        ReglasEnviosInternos.TryLeer(codigo, out _, out int b);
                        MovimientoBultoReparto carga = MovimientoEnvioInterno(e, b, TiposMovimientoBulto.CARGADO, usuario, fechaHora, momento,
                            NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, codigo, ReglaCargaObligatoria.TIPO_CLAVE_CARGA_AL_ENTREGAR));
                        movimientos.Add(carga);
                        historia.Add(carga);
                    }
                    break;
            }

            EvaluacionBultosEntrega evaluacion = EntregaIncompletaReparto.EvaluarCodigos(envioId, Math.Max(e.Bultos, 1), n => ReglasEnviosInternos.Codigo(id, n),
                CodigoBultoFurgoneta.EI, historia, null, false, leidos, usuario, peticion.SoloLeidos == true);
            foreach (BultoEntregaEvaluado b in evaluacion.Bultos.Where(b => b.Entregado && (b.Ultimo == null || b.Ultimo.Tipo != TiposMovimientoBulto.ENTREGADO)))
            {
                MovimientoBultoReparto m = MovimientoEnvioInterno(e, b.Bulto, TiposMovimientoBulto.ENTREGADO, usuario, fechaHora, momento,
                    NucleoFurgoneta.ClaveDerivada(peticion.ClaveIdempotencia, b.Codigo, TiposMovimientoBulto.ENTREGADO));
                if (b.Situacion == SituacionBultoEntrega.EntregadoSinLeer)
                {
                    m.Descripcion = CierreEntregaFurgoneta.Marcar(m.Descripcion);
                }
                movimientos.Add(m);
            }
            if (movimientos.Count > 0 && FurgonetaDisponible())
            {
                _ = await paradas.Furgoneta.Registrar(movimientos).ConfigureAwait(false);
            }
            string detalle = evaluacion.Incompleta ? EntregaIncompletaReparto.Resumen(evaluacion) : null;
            if (detalle != null && detalle.Length > 200)
            {
                detalle = detalle.Substring(0, 200);
            }
            if (!await paradas.EnviosInternos.Entregar(id, peticion.ClaveIdempotencia, fechaHora, usuario, receptor, detalle).ConfigureAwait(false))
            {
                EnvioInternoReparto ahoraEs = await paradas.EnviosInternos.Leer(id).ConfigureAwait(false);
                return ahoraEs?.ClaveEntrega == peticion.ClaveIdempotencia
                    ? ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(entregado)
                    : Conflicto($"El envío interno {ReglasEnviosInternos.Documento(id)} ha cambiado mientras: actualice la lista.");
            }
            entregado.EsNueva = true;
            entregado.BultosEntregados = evaluacion.Entregados;
            if (evaluacion.Incompleta)
            {
                entregado.BultosSinEntregar = evaluacion.Faltan.Select(b => b.Codigo).ToList();
                entregado.BultosQueFaltan = evaluacion.Faltan.Select(b => new BultoSinEntregarDTO
                {
                    Codigo = b.Codigo,
                    Bulto = b.Bulto,
                    Situacion = b.Situacion.ToString(),
                    Motivo = b.Motivo
                }).ToList();
                entregado.ResumenEntregaIncompleta = EntregaIncompletaReparto.Resumen(evaluacion);
            }
            return ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(entregado);
        }
    }
}
