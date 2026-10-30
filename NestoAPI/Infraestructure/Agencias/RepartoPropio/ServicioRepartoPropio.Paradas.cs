using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Infraestructure.NotasEntrega;
using NestoAPI.Infraestructure.PreparacionAlmacen;
using NestoAPI.Models;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>NestoAPI#759: un código escaneado ya convertido en envío y bulto, o el error de usted.</summary>
    internal class CodigoParadaResuelto
    {
        public int Envio { get; set; }
        public int Bulto { get; set; }
        public TipoResultadoReparto? Error { get; set; }
        public string Mensaje { get; set; }

        public static CodigoParadaResuelto Mal(TipoResultadoReparto tipo, string mensaje) => new CodigoParadaResuelto { Error = tipo, Mensaje = mensaje };
    }

    /// <summary>
    /// NestoAPI#759 (corte 3 de #755): las paradas. Envíos y notas (las dos son envíos de la agencia 14) y, petición de Miguel, las
    /// reposiciones a las tiendas: al cargar, un «R{traspaso}-{N}» crea o asigna la parada «Reposición {tienda}» del día (UNA por
    /// tienda destino y día aunque lleve varios traspasos), que sale en la lista con TipoParada «Reposicion» y EnvioId negativo,
    /// se ordena igual y se cierra con Entregar (sin cobro ni firma obligatoria). La recepción en la tienda sigue como siempre.
    /// Lo común (dueño, visibilidad, orden, mensaje de bulto ajeno) está en <see cref="NucleoParadasReparto"/>.
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        public const string MENSAJE_FALTA_SCRIPT_PARADAS = "Falta lanzar el script Scripts/Issue759_ParadasReparto.sql (paradas del reparto): " +
            "hasta entonces no se puede ordenar la lista ni cargar reposiciones. Avise a Informática.";
        internal const int MAXIMO_PARADAS_ORDEN = 500;

        private static readonly string[] TIENDAS = { Constantes.Almacenes.REINA, Constantes.Almacenes.ALCOBENDAS };

        // ---------------------------------------------------------------- orden

        /// <summary>
        /// PUT api/RepartoPropio/Orden: Orden = posición (1..n) de cada parada de la lista. Solo las que siguen en reparto y son del
        /// usuario o de nadie; si ya hay un orden con FechaHora más nueva, esa parada se ignora (la cola de Ariadna llega tarde).
        /// </summary>
        public async Task<ResultadoReparto<ResultadoOrdenRepartoDTO>> GuardarOrden(OrdenRepartoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            if (paradas.Orden == null || !paradas.Orden.ScriptAplicado())
            {
                return ResultadoReparto<ResultadoOrdenRepartoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_PARADAS);
            }
            if (peticion?.Envios == null)
            {
                return ResultadoReparto<ResultadoOrdenRepartoDTO>.Mal(TipoResultadoReparto.Invalido, "Falta la lista de paradas (Envios) en el orden.");
            }
            if (peticion.Envios.Count > MAXIMO_PARADAS_ORDEN)
            {
                return ResultadoReparto<ResultadoOrdenRepartoDTO>.Mal(TipoResultadoReparto.Invalido, $"No se pueden ordenar más de {MAXIMO_PARADAS_ORDEN} paradas a la vez.");
            }
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return ResultadoReparto<ResultadoOrdenRepartoDTO>.Mal(TipoResultadoReparto.Invalido, "No se sabe quién ordena las paradas.");
            }
            DateTime fechaHora = peticion.FechaHora ?? ahora();

            var posiciones = new List<OrdenParadaReparto>();
            var vistas = new HashSet<int>();
            for (int i = 0; i < peticion.Envios.Count; i++)
            {
                int id = peticion.Envios[i];
                if (id != 0 && vistas.Add(id))
                {
                    posiciones.Add(new OrdenParadaReparto { ParadaId = id, Orden = i + 1, Usuario = usuario.Trim(), FechaHora = fechaHora });
                }
            }

            Dictionary<int, string> enReparto = await DuenosDeParadasEnReparto(posiciones.Select(p => p.ParadaId).ToList()).ConfigureAwait(false);
            List<OrdenParadaReparto> candidatas = posiciones
                .Where(p => enReparto.TryGetValue(p.ParadaId, out string dueno) && !NucleoParadasReparto.EsDeOtro(dueno, usuario))
                .ToList();
            Dictionary<int, OrdenParadaReparto> existentes = (await paradas.Orden.Leer(candidatas.Select(c => c.ParadaId).ToList()).ConfigureAwait(false)
                ?? new List<OrdenParadaReparto>()).GroupBy(o => o.ParadaId).ToDictionary(g => g.Key, g => g.First());
            List<OrdenParadaReparto> aGuardar = candidatas
                .Where(c => !existentes.TryGetValue(c.ParadaId, out OrdenParadaReparto previa) || previa.FechaHora <= fechaHora)
                .ToList();
            HashSet<int> guardadas = aGuardar.Count == 0 ? new HashSet<int>()
                : await paradas.Orden.Guardar(aGuardar).ConfigureAwait(false) ?? new HashSet<int>();

            var resultado = ResultadoReparto<ResultadoOrdenRepartoDTO>.Bien(new ResultadoOrdenRepartoDTO
            {
                Guardados = guardadas.Count,
                Ignorados = peticion.Envios.Count - guardadas.Count
            });
            if (guardadas.Count > 0)
            {
                Avisar(resultado.Tipo, usuario, RepartoCambiadoDTO.MOTIVO_ORDEN, origen);
            }
            return resultado;
        }

        /// <summary>De las paradas pedidas, las que siguen en reparto, con su dueño (null si nadie las ha cargado).</summary>
        private async Task<Dictionary<int, string>> DuenosDeParadasEnReparto(IReadOnlyCollection<int> ids)
        {
            var duenos = new Dictionary<int, string>();
            List<int> envios = ids.Where(i => i > 0).ToList();
            if (envios.Count > 0)
            {
                List<EstadoEnvioParada> estados = await paradas.Orden.LeerEstadosEnvios(envios).ConfigureAwait(false) ?? new List<EstadoEnvioParada>();
                List<int> activos = estados
                    .Where(e => e.Agencia == Constantes.Agencias.AGENCIA_REPARTO_PROPIO
                        && (e.Estado == Constantes.Agencias.ESTADO_TRAMITADO || e.Estado == Constantes.Agencias.ESTADO_INCIDENTADO))
                    .Select(e => e.Numero).ToList();
                List<CargaBultoReparto> cargas = CargaDisponible() && activos.Count > 0
                    ? await carga.LeerCargas(activos).ConfigureAwait(false) ?? new List<CargaBultoReparto>()
                    : new List<CargaBultoReparto>();
                foreach (int envio in activos)
                {
                    duenos[envio] = Repartidor(cargas.Where(c => c.EnvioId == envio));
                }
            }
            List<int> reposiciones = ids.Where(NucleoParadasReparto.EsReposicion).Select(NucleoParadasReparto.IdReposicion).ToList();
            if (reposiciones.Count > 0 && ReposicionesDisponibles())
            {
                List<ParadaReposicion> abiertas = (await paradas.Reposiciones.LeerParadas(reposiciones).ConfigureAwait(false) ?? new List<ParadaReposicion>())
                    .Where(p => !p.Entregada).ToList();
                List<CargaBultoParadaReposicion> cargas = await LeerCargasReposicion(abiertas.Select(p => p.Id).ToList()).ConfigureAwait(false);
                foreach (ParadaReposicion parada in abiertas)
                {
                    duenos[NucleoParadasReparto.EnvioIdReposicion(parada.Id)] = DuenoReposicion(cargas.Where(c => c.ParadaId == parada.Id));
                }
            }
            return duenos;
        }

        /// <summary>El Orden guardado de cada parada de la lista (sin el script, nada).</summary>
        private async Task CompletarOrden(List<EnvioRepartoDTO> lista)
        {
            if (lista.Count == 0 || paradas.Orden == null || !paradas.Orden.ScriptAplicado())
            {
                return;
            }
            List<OrdenParadaReparto> ordenes = await paradas.Orden.Leer(lista.Select(p => p.EnvioId).ToList()).ConfigureAwait(false)
                ?? new List<OrdenParadaReparto>();
            foreach (EnvioRepartoDTO parada in lista)
            {
                parada.Orden = ordenes.FirstOrDefault(o => o.ParadaId == parada.EnvioId)?.Orden;
            }
        }

        // ---------------------------------------------------------------- códigos y notas

        /// <summary>«R81010-2» (y no RP/NE/NR): el bulto de una reposición.</summary>
        internal static bool EsCodigoReposicion(string codigo, out int traspaso, out int bulto)
        {
            traspaso = bulto = 0;
            string limpio = codigo?.Trim();
            if (string.IsNullOrEmpty(limpio) || CodigoBultoRepartoPropio.TryLeerBulto(limpio, out _, out _) || CodigoNotaEntrega.TryLeer(limpio, out _, out _, out _))
            {
                return false;
            }
            return CodigosBultoReposicion.Leer(limpio, out traspaso, out bulto);
        }

        /// <summary>RP{envío}-{N} tal cual; NE/NR, al envío de la parada de su nota (Bulto 1, o el N de NR…-N).</summary>
        private async Task<CodigoParadaResuelto> ResolverCodigo(string codigo)
        {
            if (CodigoBultoRepartoPropio.TryLeerBulto(codigo, out int numero, out int bulto))
            {
                return new CodigoParadaResuelto { Envio = numero, Bulto = bulto };
            }
            if (CodigoNotaEntrega.TryLeer(codigo, out _, out int pedido, out _) && paradas.Notas != null)
            {
                ResolucionCodigoNota nota = await paradas.Notas.Resolver(codigo).ConfigureAwait(false);
                if (nota == null || !nota.EsNota)
                {
                    return CodigoParadaResuelto.Mal(TipoResultadoReparto.NoEncontrado, $"El código «{codigo?.Trim()}» no es de ninguna nota de entrega.");
                }
                if (nota.EnvioId.HasValue)
                {
                    return new CodigoParadaResuelto { Envio = nota.EnvioId.Value, Bulto = nota.Bulto };
                }
                return nota.EnvioEntregado.HasValue
                    ? CodigoParadaResuelto.Mal(TipoResultadoReparto.Conflicto, $"La nota {pedido} ya está {(nota.EsRecogida ? "recogida" : "entregada")}.")
                    : CodigoParadaResuelto.Mal(TipoResultadoReparto.NoEncontrado, $"La nota {pedido} no está en el reparto: factúrela en Facturar rutas.");
            }
            return CodigoParadaResuelto.Mal(TipoResultadoReparto.NoEncontrado,
                $"El código «{codigo?.Trim()}» no es de un bulto del reparto propio (RP…, NE…, NR… o R… de una reposición).");
        }

        /// <summary>Las notas de esos pedidos, por pedido (sin el repositorio, ninguna).</summary>
        private async Task<Dictionary<int, NotaParadaReparto>> NotasDe(IEnumerable<int?> pedidos)
        {
            List<int> lista = (pedidos ?? Enumerable.Empty<int?>()).Where(p => p.HasValue && p.Value > 0).Select(p => p.Value).Distinct().ToList();
            if (lista.Count == 0 || paradas.NotasParada == null)
            {
                return new Dictionary<int, NotaParadaReparto>();
            }
            return (await paradas.NotasParada.LeerNotas(lista).ConfigureAwait(false) ?? new List<NotaParadaReparto>())
                .GroupBy(n => n.Pedido).ToDictionary(g => g.Key, g => g.First());
        }

        private async Task<NotaParadaReparto> NotaDe(int? pedido)
            => pedido.HasValue && (await NotasDe(new[] { pedido }).ConfigureAwait(false)).TryGetValue(pedido.Value, out NotaParadaReparto nota) ? nota : null;

        private async Task<NotaParadaReparto> NotaDeEnvio(int envioId)
        {
            if (paradas.NotasParada == null)
            {
                return null;
            }
            EnviosAgencia envio = await repositorio.LeerEnvio(envioId).ConfigureAwait(false);
            return await NotaDe(envio?.Pedido).ConfigureAwait(false);
        }

        /// <summary>TipoParada, TextoNota, NumeroNota y CodigoDocumento de los envíos que son notas.</summary>
        private async Task CompletarNotas(List<EnvioRepartoDTO> lista)
        {
            Dictionary<int, NotaParadaReparto> notas = await NotasDe(lista.Select(e => e.Pedido)).ConfigureAwait(false);
            foreach (EnvioRepartoDTO envio in lista)
            {
                if (envio.Pedido.HasValue && notas.TryGetValue(envio.Pedido.Value, out NotaParadaReparto nota))
                {
                    envio.TipoParada = nota.TipoParada;
                    envio.TextoNota = nota.Texto;
                    envio.NumeroNota = nota.NumeroNota;
                    envio.CodigoDocumento = nota.CodigoDocumento;
                }
            }
        }

        // ---------------------------------------------------------------- reposiciones

        private bool ReposicionesDisponibles() => paradas.Reposiciones != null && paradas.Reposiciones.ScriptAplicado();

        private static string DuenoReposicion(IEnumerable<CargaBultoParadaReposicion> cargas)
            => NucleoParadasReparto.Propietario(cargas, c => c.Id, c => c.Usuario);

        private async Task<List<CargaBultoParadaReposicion>> LeerCargasReposicion(IReadOnlyCollection<int> paradasIds)
            => paradasIds.Count == 0 ? new List<CargaBultoParadaReposicion>()
                : await paradas.Reposiciones.LeerCargas(paradasIds).ConfigureAwait(false) ?? new List<CargaBultoParadaReposicion>();

        /// <summary>«Reina», «Alcobendas»; el código si no es una tienda.</summary>
        internal static string NombreTienda(string almacen)
        {
            switch (almacen?.Trim().ToUpperInvariant())
            {
                case Constantes.Almacenes.REINA: return "Reina";
                case Constantes.Almacenes.ALCOBENDAS: return "Alcobendas";
                case Constantes.Almacenes.ALGETE: return "Algete";
                default: return almacen?.Trim();
            }
        }

        /// <summary>Las paradas de reposición sin entregar de los últimos 30 días, como paradas de la lista.</summary>
        private async Task<List<EnvioRepartoDTO>> ParadasReposicionEnReparto(DateTime dia)
        {
            if (!ReposicionesDisponibles())
            {
                return new List<EnvioRepartoDTO>();
            }
            List<ParadaReposicion> abiertas = await paradas.Reposiciones.LeerEnReparto(dia.AddDays(-DIAS_ATRAS_LISTA), dia.AddDays(1)).ConfigureAwait(false)
                ?? new List<ParadaReposicion>();
            List<CargaBultoParadaReposicion> cargas = await LeerCargasReposicion(abiertas.Select(p => p.Id).ToList()).ConfigureAwait(false);
            return abiertas
                .Select(p => (EnvioRepartoDTO)DtoReposicion(p, cargas.Where(c => c.ParadaId == p.Id).ToList(), new EnvioRepartoDTO()))
                .ToList();
        }

        private static T DtoReposicion<T>(ParadaReposicion parada, List<CargaBultoParadaReposicion> cargas, T dto) where T : EnvioRepartoDTO
        {
            string dueno = DuenoReposicion(cargas);
            string tienda = NombreTienda(parada.Destino);
            var porTraspaso = cargas.GroupBy(c => c.Traspaso).OrderBy(g => g.Key).ToList();
            dto.EnvioId = NucleoParadasReparto.EnvioIdReposicion(parada.Id);
            dto.Empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            dto.Nombre = "Reposición " + tienda;
            dto.Poblacion = tienda;
            dto.Bultos = porTraspaso.Sum(g => g.Max(c => Math.Max(c.Total, c.Bulto)));
            dto.Reembolso = 0m;
            dto.Estado = parada.Entregada ? EnvioRepartoDTO.ESTADO_ENTREGADO : EnvioRepartoDTO.ESTADO_TRAMITADO;
            dto.TipoParada = TiposParadaReparto.REPOSICION;
            dto.TextoNota = porTraspaso.Count == 0 ? null : string.Join("\n", porTraspaso.Select(g =>
            {
                int total = g.Max(c => Math.Max(c.Total, c.Bulto));
                int cargados = g.Select(c => c.Bulto).Distinct().Count();
                return $"Reposición {g.Key}: {cargados} de {total} {(total == 1 ? "bulto" : "bultos")}";
            }));
            dto.Repartidor = NombreRepartidor(dueno);
            dto.BultosCargados = NucleoParadasReparto.BultosCargados(cargas, dueno, c => c.Usuario, c => (c.Traspaso, c.Bulto));
            dto.FechaParada = parada.Fecha;
            // NestoAPI#767: la tienda, con su dirección real (la de la web, TiendasRecogida) y sus coordenadas fijas, para «Ir» y
            // «Ruta completa» en Ariadna.
            PedidosVenta.TiendasRecogida.Tienda datosTienda = PedidosVenta.TiendasRecogida.Buscar(parada.Destino);
            if (datosTienda != null)
            {
                dto.Direccion = datosTienda.Direccion;
                dto.CodigoPostal = datosTienda.CodigoPostal;
                dto.Poblacion = datosTienda.Poblacion;
                dto.Telefono = datosTienda.Telefono;
            }
            CoordenadasGeo tiendaGeo = SedesReparto.DeAlmacen(parada.Destino);
            dto.Latitud = tiendaGeo?.Latitud;
            dto.Longitud = tiendaGeo?.Longitud;
            return dto;
        }

        /// <summary>GET Bulto/R81010-2: la parada de reposición en la que está cargado ese bulto.</summary>
        private async Task<ResultadoReparto<EnvioRepartoBultoDTO>> BultoReposicion(int traspaso, int bulto)
        {
            if (!ReposicionesDisponibles())
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_PARADAS);
            }
            string codigo = CodigosBultoReposicion.Codigo(traspaso, bulto);
            CargaBultoParadaReposicion cargada = await paradas.Reposiciones.LeerCargaDelBulto(Constantes.Empresas.EMPRESA_POR_DEFECTO, traspaso, bulto).ConfigureAwait(false);
            ParadaReposicion parada = cargada == null ? null
                : (await paradas.Reposiciones.LeerParadas(new[] { cargada.ParadaId }).ConfigureAwait(false))?.FirstOrDefault();
            if (parada == null)
            {
                return ResultadoReparto<EnvioRepartoBultoDTO>.Mal(TipoResultadoReparto.NoEncontrado,
                    $"El bulto {codigo} no está cargado en ninguna parada: cárguelo antes en la furgoneta.");
            }
            EnvioRepartoBultoDTO dto = DtoReposicion(parada, await LeerCargasReposicion(new[] { parada.Id }).ConfigureAwait(false), new EnvioRepartoBultoDTO());
            dto.Bulto = bulto;
            await CompletarOrden(new List<EnvioRepartoDTO> { dto }).ConfigureAwait(false);
            return ResultadoReparto<EnvioRepartoBultoDTO>.Bien(dto);
        }

        /// <summary>
        /// Cargar un R{traspaso}-{N}: el bulto tiene que ser de una reposición enviada (ReposicionesBultos), sin recibir todavía y
        /// a una tienda (Reina o Alcobendas). Va a la parada sin entregar de esa tienda y ese día (la crea si no hay).
        /// </summary>
        private async Task<ResultadoReparto<ResultadoCargaBultoDTO>> CargarReposicion(CargaBultoRepartoDTO peticion, int traspaso, int bulto, string usuario)
        {
            if (!ReposicionesDisponibles() || paradas.BultosReposicion == null)
            {
                return MalCarga(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_PARADAS);
            }
            string empresa = Constantes.Empresas.EMPRESA_POR_DEFECTO;
            string codigo = CodigosBultoReposicion.Codigo(traspaso, bulto);

            // Misma clave = misma respuesta (la cola de Ariadna reintenta).
            CargaBultoParadaReposicion previa = await paradas.Reposiciones.LeerCargaPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
            if (previa != null)
            {
                return previa.Traspaso == traspaso && previa.Bulto == bulto
                    ? await RespuestaReposicion(previa.ParadaId, bulto, previa.Usuario).ConfigureAwait(false)
                    : MalCarga(TipoResultadoReparto.Conflicto, $"Esa ClaveIdempotencia ya se usó para cargar el bulto {CodigosBultoReposicion.Codigo(previa.Traspaso, previa.Bulto)}. " +
                        "Cada lectura lleva su propia clave.");
            }

            FilaBultoReposicion etiqueta = await paradas.BultosReposicion.LeerPorCodigo(empresa, codigo).ConfigureAwait(false);
            if (etiqueta == null)
            {
                return MalCarga(TipoResultadoReparto.NoEncontrado, $"El bulto {codigo} no es de ninguna reposición enviada: compruebe la etiqueta.");
            }
            if (etiqueta.FechaLectura.HasValue)
            {
                return MalCarga(TipoResultadoReparto.Conflicto, $"La reposición {traspaso} ya se ha recibido en la tienda: no hay que cargarla.");
            }
            OrigenDestinoReposicion origenDestino = await paradas.BultosReposicion.LeerOrigenDestino(empresa, traspaso).ConfigureAwait(false);
            string destino = origenDestino?.Destino?.Trim().ToUpperInvariant();
            if (destino == null || !TIENDAS.Contains(destino))
            {
                return MalCarga(TipoResultadoReparto.Conflicto,
                    $"La reposición {traspaso} no va a una tienda{(destino == null ? string.Empty : $" (va a {NombreTienda(destino)})")}: no es una parada del reparto.");
            }

            DateTime momento = ahora();
            DateTime dia = (peticion.FechaHora ?? momento).Date;
            ParadaReposicion parada = null;
            CargaBultoParadaReposicion yaCargado = await paradas.Reposiciones.LeerCargaDelBulto(empresa, traspaso, bulto).ConfigureAwait(false);
            if (yaCargado != null)
            {
                parada = (await paradas.Reposiciones.LeerParadas(new[] { yaCargado.ParadaId }).ConfigureAwait(false))?.FirstOrDefault();
                if (parada != null && parada.Entregada)
                {
                    return MalCarga(TipoResultadoReparto.Conflicto, $"El bulto {codigo} ya se entregó en {NombreTienda(parada.Destino)}.");
                }
            }
            if (parada == null)
            {
                parada = await paradas.Reposiciones.LeerAbierta(dia, destino).ConfigureAwait(false);
            }
            List<CargaBultoParadaReposicion> cargas = parada == null ? new List<CargaBultoParadaReposicion>()
                : await LeerCargasReposicion(new[] { parada.Id }).ConfigureAwait(false);
            string actual = DuenoReposicion(cargas);
            if (NucleoParadasReparto.EsDeOtro(actual, usuario) && !peticion.Reasignar)
            {
                return NucleoParadasReparto.ConflictoDeOtro<ResultadoCargaBultoDTO>(actual);
            }
            if (!NucleoParadasReparto.EsDeOtro(actual, usuario) && actual != null
                && cargas.Any(c => c.Traspaso == traspaso && c.Bulto == bulto && MismoUsuario(c.Usuario, usuario)))
            {
                // El mismo bulto otra vez (otra clave): no es un error y no se apunta dos veces.
                return await RespuestaReposicion(parada.Id, bulto, usuario).ConfigureAwait(false);
            }

            var nueva = new CargaBultoParadaReposicion
            {
                ParadaId = parada?.Id ?? 0,
                Empresa = empresa,
                Traspaso = traspaso,
                Bulto = bulto,
                Total = etiqueta.Total,
                Usuario = usuario.Trim(),
                FechaHora = peticion.FechaHora ?? momento,
                ClaveIdempotencia = peticion.ClaveIdempotencia,
                FechaRegistro = momento
            };
            int paradaId;
            try
            {
                paradaId = await paradas.Reposiciones.Cargar(nueva, dia, destino).ConfigureAwait(false);
            }
            catch (ClaveIdempotenciaRepetidaException)
            {
                CargaBultoParadaReposicion ganadora = await paradas.Reposiciones.LeerCargaPorClave(peticion.ClaveIdempotencia).ConfigureAwait(false);
                return ganadora != null
                    ? await RespuestaReposicion(ganadora.ParadaId, bulto, ganadora.Usuario).ConfigureAwait(false)
                    : MalCarga(TipoResultadoReparto.Conflicto, "Esa carga ya se está registrando. Vuelva a intentarlo.");
            }
            return await RespuestaReposicion(paradaId, bulto, usuario).ConfigureAwait(false);
        }

        private async Task<ResultadoReparto<ResultadoCargaBultoDTO>> RespuestaReposicion(int paradaId, int bulto, string usuario)
        {
            ParadaReposicion parada = (await paradas.Reposiciones.LeerParadas(new[] { paradaId }).ConfigureAwait(false))?.FirstOrDefault();
            if (parada == null)
            {
                return MalCarga(TipoResultadoReparto.NoEncontrado, $"No existe la parada de reposición {paradaId}.");
            }
            EnvioRepartoDTO dto = DtoReposicion(parada, await LeerCargasReposicion(new[] { paradaId }).ConfigureAwait(false), new EnvioRepartoDTO());
            return ResultadoReparto<ResultadoCargaBultoDTO>.Bien(new ResultadoCargaBultoDTO
            {
                EnvioId = dto.EnvioId,
                Bulto = bulto,
                Bultos = dto.Bultos,
                BultosCargados = dto.BultosCargados,
                Repartidor = dto.Repartidor ?? NombreRepartidor(usuario),
                Nombre = dto.Nombre,
                Poblacion = dto.Poblacion,
                TipoParada = TiposParadaReparto.REPOSICION
            });
        }

        /// <summary>Entregar una parada de reposición: la cierra (sin cobro, sin firma obligatoria). Idempotente por la clave.</summary>
        private async Task<ResultadoReparto<EstadoEnvioRepartoDTO>> EntregarReposicion(int id, EntregaRepartoDTO peticion, string usuario)
        {
            if (!ReposicionesDisponibles())
            {
                return Invalido(MENSAJE_FALTA_SCRIPT_PARADAS);
            }
            if (peticion == null || peticion.ClaveIdempotencia == Guid.Empty)
            {
                return Invalido("Falta la ClaveIdempotencia de la entrega.");
            }
            if (!string.IsNullOrWhiteSpace(peticion.CobroReembolso)
                && !string.Equals(peticion.CobroReembolso.Trim(), PruebaEntregaReparto.COBRO_NO_COBRADO, StringComparison.OrdinalIgnoreCase))
            {
                return Invalido("Una parada de reposición no lleva cobro.");
            }
            string receptor = Limpio(peticion.Receptor);
            string observaciones = Limpio(peticion.Observaciones);
            if (receptor != null && receptor.Length > MAXIMO_RECEPTOR)
            {
                return Invalido($"El nombre de quien recoge no puede pasar de {MAXIMO_RECEPTOR} caracteres.");
            }
            if (observaciones != null && observaciones.Length > MAXIMO_TEXTO)
            {
                return Invalido($"Las observaciones no pueden pasar de {MAXIMO_TEXTO} caracteres.");
            }
            ParadaReposicion parada = (await paradas.Reposiciones.LeerParadas(new[] { id }).ConfigureAwait(false))?.FirstOrDefault();
            if (parada == null)
            {
                return ResultadoReparto<EstadoEnvioRepartoDTO>.Mal(TipoResultadoReparto.NoEncontrado, "No existe esa parada de reposición del reparto propio.");
            }
            var entregada = ResultadoReparto<EstadoEnvioRepartoDTO>.Bien(new EstadoEnvioRepartoDTO
            {
                EnvioId = NucleoParadasReparto.EnvioIdReposicion(id),
                Estado = EnvioRepartoDTO.ESTADO_ENTREGADO
            });
            if (!parada.Entregada)
            {
                bool hecho = await paradas.Reposiciones.Entregar(id, peticion.ClaveIdempotencia, peticion.FechaHora ?? ahora(), usuario, receptor, observaciones,
                    peticion.Latitud, peticion.Longitud).ConfigureAwait(false);
                if (hecho)
                {
                    return entregada;
                }
                parada = (await paradas.Reposiciones.LeerParadas(new[] { id }).ConfigureAwait(false))?.FirstOrDefault() ?? parada;
            }
            return parada.ClaveEntrega == peticion.ClaveIdempotencia
                ? entregada
                : Conflicto($"La parada «Reposición {NombreTienda(parada.Destino)}» ya está entregada.");
        }

        // ---------------------------------------------------------------- avisos

        /// <summary>SignalR (AlmacenHub, grupo «Reparto»): solo si ha ido bien. Nunca rompe la respuesta.</summary>
        private void Avisar(TipoResultadoReparto tipo, string usuario, string motivo, OrigenPeticionReparto origen)
        {
            if (tipo != TipoResultadoReparto.Ok || paradas.Avisos == null)
            {
                return;
            }
            try
            {
                paradas.Avisos.RepartoCambiado(usuario, motivo, ahora(), origen);
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto propio #759] No se pudo avisar del cambio ({motivo}): {ex.Message}", ex));
            }
        }
    }
}
