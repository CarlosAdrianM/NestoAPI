using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NestoAPI.Models.Agencias;

namespace NestoAPI.Infraestructure.Agencias.RepartoPropio
{
    /// <summary>
    /// NestoAPI#767 (Ariadna#68): coordenadas de las paradas y orden sugerido. Las coordenadas salen SOLO de la caché
    /// (DireccionesCoordenadas); lo que falta lo busca un job en segundo plano con Nominatim y, mientras, esa parada va detrás.
    /// El recorrido sale de la posición ACTUAL de la PDA (o de Algete si no la manda), pasa por las paradas PENDIENTES del
    /// repartidor y acaba en Algete o en el destino que mande la PDA (ese punto no se guarda en ningún sitio). El orden se guarda con el mismo mecanismo que PUT Orden (último gana, aviso «repartoCambiado»).
    /// </summary>
    public partial class ServicioRepartoPropio
    {
        /// <summary>Latitud y Longitud de cada parada de envío/nota desde la caché (las de reposición ya traen las de su tienda).</summary>
        private async Task CompletarCoordenadas(List<EnvioRepartoDTO> lista)
        {
            if (paradas.Coordenadas == null || lista.Count == 0)
            {
                return;
            }
            var direcciones = new Dictionary<EnvioRepartoDTO, DireccionParada>();
            foreach (EnvioRepartoDTO parada in lista.Where(p => !NucleoParadasReparto.EsReposicion(p.EnvioId)))
            {
                direcciones[parada] = new DireccionParada(parada.Direccion, parada.CodigoPostal, parada.Poblacion);
            }
            if (direcciones.Count == 0)
            {
                return;
            }
            Dictionary<string, CoordenadasGeo> encontradas;
            try
            {
                encontradas = await paradas.Coordenadas.Leer(direcciones.Values.ToList()).ConfigureAwait(false) ?? new Dictionary<string, CoordenadasGeo>();
            }
            catch (Exception ex)
            {
                // Las coordenadas son un añadido: sin ellas la lista sale igual.
                ElmahHelper.Log(new Exception($"[Reparto propio #767] No se pudieron leer las coordenadas de las paradas: {ex.Message}", ex));
                return;
            }
            bool faltan = false;
            foreach (KeyValuePair<EnvioRepartoDTO, DireccionParada> par in direcciones)
            {
                if (encontradas.TryGetValue(par.Value.Clave, out CoordenadasGeo geo))
                {
                    par.Key.Latitud = geo.Latitud;
                    par.Key.Longitud = geo.Longitud;
                    par.Key.CoordenadasAproximadas = geo.PorCodigoPostal; // NestoAPI#881
                }
                else if (par.Value.Geocodificable)
                {
                    faltan = true;
                }
            }
            if (faltan)
            {
                paradas.Coordenadas.PedirGeocodificacion();
            }
        }

        /// <summary>
        /// POST api/RepartoPropio/OrdenSugerido. Solo las paradas del que llama (las que ha cargado) que siguen pendientes
        /// (Tramitado): las incidentadas ya se han visitado y van al final; las entregadas ya no están. Orden final: pendientes con
        /// coordenadas por el recorrido, pendientes sin coordenadas en su orden de antes, incidentadas.
        /// </summary>
        public async Task<ResultadoReparto<OrdenSugeridoRepartoDTO>> OrdenSugerido(PeticionOrdenSugeridoDTO peticion, string usuario, OrigenPeticionReparto origen = null)
        {
            if (paradas.Orden == null || !paradas.Orden.ScriptAplicado())
            {
                return ResultadoReparto<OrdenSugeridoRepartoDTO>.Mal(TipoResultadoReparto.Invalido, MENSAJE_FALTA_SCRIPT_PARADAS);
            }
            if (string.IsNullOrWhiteSpace(usuario))
            {
                return ResultadoReparto<OrdenSugeridoRepartoDTO>.Mal(TipoResultadoReparto.Invalido, "No se sabe quién pide el orden de las paradas.");
            }
            peticion = peticion ?? new PeticionOrdenSugeridoDTO();
            string error = LeerExtremos(peticion, out CoordenadasGeo inicio, out CoordenadasGeo final, out bool conDestino);
            if (error != null)
            {
                return ResultadoReparto<OrdenSugeridoRepartoDTO>.Mal(TipoResultadoReparto.Invalido, error);
            }
            ConstantesTiemposReparto constantes = ConstantesTiempos(); // NestoAPI#881

            ResultadoReparto<List<EnvioRepartoDTO>> lista = await Envios(peticion.Fecha, soloMios: true, usuario: usuario).ConfigureAwait(false);
            if (lista.Tipo != TipoResultadoReparto.Ok)
            {
                return new ResultadoReparto<OrdenSugeridoRepartoDTO> { Tipo = lista.Tipo, Mensaje = lista.Mensaje, Codigo = lista.Codigo, Detalles = lista.Detalles };
            }
            List<EnvioRepartoDTO> mias = (lista.Contenido ?? new List<EnvioRepartoDTO>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Repartidor) && MismoUsuario(p.Repartidor, usuario))
                .ToList();
            List<EnvioRepartoDTO> pendientes = mias.Where(p => p.Estado == EnvioRepartoDTO.ESTADO_TRAMITADO).ToList();
            List<EnvioRepartoDTO> visitadas = mias.Where(p => p.Estado != EnvioRepartoDTO.ESTADO_TRAMITADO).ToList();
            List<EnvioRepartoDTO> conCoordenadas = pendientes.Where(p => CoordenadasGeo.Validas(p.Latitud, p.Longitud)).ToList();
            List<EnvioRepartoDTO> sinCoordenadas = pendientes.Where(p => !CoordenadasGeo.Validas(p.Latitud, p.Longitud)).ToList();

            CoordenadasGeo salida = inicio ?? SedesReparto.ALGETE;
            var respuesta = new OrdenSugeridoRepartoDTO
            {
                Origen = inicio != null ? OrdenSugeridoRepartoDTO.ORIGEN_POSICION : OrdenSugeridoRepartoDTO.ORIGEN_ALMACEN,
                OrigenLatitud = salida.Latitud,
                OrigenLongitud = salida.Longitud,
                Destino = conDestino ? OrdenSugeridoRepartoDTO.DESTINO_PERSONALIZADO : OrdenSugeridoRepartoDTO.DESTINO_ALMACEN,
                DestinoLatitud = final.Latitud,
                DestinoLongitud = final.Longitud,
                SinCoordenadas = sinCoordenadas.Count,
                Motor = OrdenSugeridoRepartoDTO.MOTOR_SIN_COORDENADAS
            };

            List<EnvioRepartoDTO> recorrido = conCoordenadas;
            DateTime ahoraPda = peticion.FechaHora ?? ahora();
            DateTime? horaSalida = HoraSalida((peticion.Fecha ?? ahoraPda).Date, ahoraPda);
            if (conCoordenadas.Count > 0)
            {
                IPlanificadorRecorrido planificador = paradas.Recorrido ?? new PlanificadorRecorrido(null);
                RecorridoCalculado calculado = await planificador.Calcular(salida, final,
                    conCoordenadas.Select(p => new CoordenadasGeo(p.Latitud.Value, p.Longitud.Value)).ToList()).ConfigureAwait(false);
                recorrido = calculado.Orden.Select(i => conCoordenadas[i]).ToList();
                respuesta.Motor = calculado.Motor;
                respuesta.Kilometros = calculado.Kilometros;
                respuesta.Minutos = calculado.Minutos;

                // NestoAPI#763: a qué hora se llega a cada una y cuáles caen fuera de su horario preferible (solo se avisa). NestoAPI#881:
                // con los minutos de cada parada y el colchón de la conducción.
                if (horaSalida.HasValue)
                {
                    respuesta.FueraDeHorario = EstimarLlegadas(recorrido, salida, horaSalida.Value, calculado.MinutosTramos,
                        MinutosParadaReparto.Recorrido(recorrido, constantes), constantes.FactorConduccion);
                    respuesta.AvisoHorario = AvisoHorario(recorrido.Where(p => p.FueraDeHorario));
                    // NestoAPI#769: si alguna llega fuera, se intenta otro orden (sin compromiso).
                    recorrido = await ReordenarPorHorario(recorrido, salida, final, horaSalida.Value, respuesta, constantes).ConfigureAwait(false);
                }
            }

            // NestoAPI#881: el fin previsto con el orden nuevo (las pendientes; las visitadas no cuentan). Nunca lo rompe.
            List<EnvioRepartoDTO> pendientesEnOrden = recorrido.Concat(sinCoordenadas).ToList();
            try
            {
                respuesta.FinRuta = await EstimarFin(pendientesEnOrden, salida, final, conDestino, horaSalida ?? ahoraPda, usuario).ConfigureAwait(false);
                await FotoEstimacion(respuesta.FinRuta, usuario, respuesta.Origen, FotoEstimacionReparto.CALCULO_ORDEN_SUGERIDO).ConfigureAwait(false);
                foreach (ParadaEstimadaDTO estimada in respuesta.FinRuta.Paradas)
                {
                    EnvioRepartoDTO parada = pendientesEnOrden.FirstOrDefault(p => p.EnvioId == estimada.EnvioId);
                    if (parada != null)
                    {
                        parada.MinutosParada = estimada.MinutosParada;
                    }
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Reparto #881] El orden sugerido está hecho, pero no se pudo calcular el fin de ruta: {ex.Message}", ex));
            }

            List<EnvioRepartoDTO> ordenadas = recorrido.Concat(sinCoordenadas).Concat(visitadas).ToList();
            if (ordenadas.Count > 0)
            {
                ResultadoReparto<ResultadoOrdenRepartoDTO> guardado = await GuardarOrden(new OrdenRepartoDTO
                {
                    FechaHora = peticion.FechaHora ?? ahora(),
                    Envios = ordenadas.Select(p => p.EnvioId).ToList()
                }, usuario, origen).ConfigureAwait(false);
                if (guardado.Tipo != TipoResultadoReparto.Ok)
                {
                    return new ResultadoReparto<OrdenSugeridoRepartoDTO> { Tipo = guardado.Tipo, Mensaje = guardado.Mensaje };
                }
                respuesta.Guardados = guardado.Contenido.Guardados;
                respuesta.Ignorados = guardado.Contenido.Ignorados;
            }
            for (int i = 0; i < ordenadas.Count; i++)
            {
                ordenadas[i].Orden = i + 1;
            }
            respuesta.Paradas = ordenadas;
            return ResultadoReparto<OrdenSugeridoRepartoDTO>.Bien(respuesta);
        }
    }
}
