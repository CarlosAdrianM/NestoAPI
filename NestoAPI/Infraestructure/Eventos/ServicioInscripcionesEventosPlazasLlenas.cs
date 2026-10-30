using NestoAPI.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Eventos
{
    /// <summary>
    /// NestoAPI#746 (política de Pilar aprobada por Carlos, 30/10/26): «se reserva sin pagar MIENTRAS HAYA PLAZAS». Con el evento
    /// lleno (las que ocupan plaza llegan a Evento.Plazas), cada reserva sin pagar recibe UN aviso con un plazo para pagar
    /// (<see cref="ParametrosPlazasLlenasEventos.HorasPago"/>, nunca después de su último día para pagar); si vence sin pagar y el
    /// evento sigue lleno, pasa a Caducada y la plaza queda libre. Los eventos sin Plazas no se tocan.
    /// </summary>
    public partial class ServicioInscripcionesEventos
    {
        internal const string MOTIVO_SIN_REPOSITORIO = "Sin repositorio de plazas llenas.";
        internal const string MOTIVO_APAGADO = "Apagado (ParametrosUsuario «(defecto)» EventosPlazasLlenasActivo).";
        internal const string MOTIVO_SIN_SCRIPT = "Faltan las columnas AvisoPlazasLlenas y PlazoPagoPlazasLlenas (script de NestoAPI#746).";

        /// <summary>Las columnas del aviso de «evento lleno». Null (tests que no lo usan): la política no avisa ni anula.</summary>
        internal IRepositorioPlazasLlenas PlazasLlenas { get; set; }

        /// <summary>Los parámetros («(defecto)»). Null: los valores por defecto (encendido, 48 h).</summary>
        internal Func<ParametrosPlazasLlenasEventos> ParametrosPlazasLlenas { get; set; }

        /// <summary>Encola la pasada de la política para ese evento (Hangfire en producción). Null: no se encola.</summary>
        internal Action<int> EncolarPlazasLlenas { get; set; }

        /// <summary>
        /// (inscripción, evento, estado en que estaba, plazas ocupadas ahora) cuando llega dinero de una inscripción Caducada o
        /// Anulada: correo a Administración y campana a quien la inscribió. Null: solo ELMAH.
        /// </summary>
        internal Func<EventoInscripcion, Evento, EstadoInscripcionEvento, int, Task> AvisarPagoFueraDePlazo { get; set; }

        /// <summary>
        /// Una pasada de la política en un evento (o en todos los activos por venir con plazas): primero las reservas avisadas
        /// con el plazo vencido (Caducada si el evento sigue lleno; si ya hay plazas libres se les quita la marca) y luego, si el
        /// evento está lleno, el aviso de las reservas sin pagar que aún no lo tenían (se apunta aquí, antes de mandarlo, para
        /// que salga una sola vez). Quien llama manda los avisos.
        /// </summary>
        internal async Task<ResultadoPlazasLlenas> ProcesarPlazasLlenas(int? eventoId)
        {
            var resultado = new ResultadoPlazasLlenas();
            if (PlazasLlenas == null)
            {
                resultado.MotivoNoSeProcesa = MOTIVO_SIN_REPOSITORIO;
                return resultado;
            }
            ParametrosPlazasLlenasEventos parametros = ParametrosPlazasLlenas?.Invoke() ?? new ParametrosPlazasLlenasEventos();
            if (!parametros.Activo)
            {
                resultado.MotivoNoSeProcesa = MOTIVO_APAGADO;
                return resultado;
            }
            if (!PlazasLlenas.Disponible())
            {
                resultado.MotivoNoSeProcesa = MOTIVO_SIN_SCRIPT;
                return resultado;
            }

            DateTime ahora = reloj();
            DateTime hoy = ahora.Date;
            IQueryable<Evento> consulta = db.Eventos.Where(e => e.Activo && e.Fecha >= hoy && e.Plazas != null);
            if (eventoId.HasValue)
            {
                int id = eventoId.Value;
                consulta = consulta.Where(e => e.Id == id);
            }
            List<Evento> eventos = await consulta.ToListAsync().ConfigureAwait(false);
            if (!eventos.Any())
            {
                return resultado;
            }
            List<int> ids = eventos.Select(e => e.Id).ToList();
            List<EventoInscripcion> inscripciones = await db.EventosInscripciones
                .Where(i => ids.Contains(i.EventoId)).ToListAsync().ConfigureAwait(false);
            Dictionary<int, MarcaPlazasLlenas> marcas = await PlazasLlenas.Leer(
                inscripciones.Where(ReglasPlazasLlenas.EsReservaSinPagar).Select(i => i.Id).ToList()).ConfigureAwait(false)
                ?? new Dictionary<int, MarcaPlazasLlenas>();

            bool cambios = false;
            foreach (Evento evento in eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Id))
            {
                List<EventoInscripcion> suyas = inscripciones.Where(i => i.EventoId == evento.Id).ToList();
                int ocupadas = suyas.Count(OcupaPlaza);
                List<EventoInscripcion> sinPagar = suyas.Where(ReglasPlazasLlenas.EsReservaSinPagar).OrderBy(i => i.FechaCreacion).ThenBy(i => i.Id).ToList();

                // 1. Plazos vencidos
                List<EventoInscripcion> vencidas = sinPagar
                    .Where(i => marcas.TryGetValue(i.Id, out MarcaPlazasLlenas m) && m.PlazoPagoPlazasLlenas.HasValue && m.PlazoPagoPlazasLlenas.Value <= ahora)
                    .ToList();
                if (vencidas.Any())
                {
                    if (ReglasPlazasLlenas.EstaLleno(evento, ocupadas))
                    {
                        foreach (EventoInscripcion vencida in vencidas)
                        {
                            Cambiar(vencida, EstadoInscripcionEvento.Caducada, USUARIO_AUTOMATICO);
                            resultado.Anuladas.Add(new AvisoPlazasLlenasInscripcion
                            {
                                Evento = evento,
                                Inscripcion = vencida,
                                Plazo = marcas[vencida.Id].PlazoPagoPlazasLlenas.Value
                            });
                            ocupadas--;
                            cambios = true;
                        }
                    }
                    else
                    {
                        // Ya hay plazas libres (alguien anuló o dijo «No podré ir»): sigue reservada con su fecha límite de
                        // siempre y, si el evento se vuelve a llenar, se le avisará otra vez.
                        foreach (EventoInscripcion vencida in vencidas)
                        {
                            await PlazasLlenas.Apuntar(vencida.Id, null, null).ConfigureAwait(false);
                            _ = marcas.Remove(vencida.Id);
                            resultado.Indultadas++;
                        }
                    }
                    sinPagar = sinPagar.Except(vencidas).ToList();
                }

                // 2. Evento lleno: aviso a las reservas sin pagar que no lo tienen
                if (ReglasPlazasLlenas.EstaLleno(evento, ocupadas))
                {
                    foreach (EventoInscripcion reserva in sinPagar.Where(i => !marcas.ContainsKey(i.Id) || marcas[i.Id].PlazoPagoPlazasLlenas == null))
                    {
                        DateTime plazo = ReglasPlazasLlenas.Plazo(ahora, parametros.HorasPago,
                            ReglasInscripcionEvento.LimitePagoEfectivo(reserva, evento), evento);
                        await PlazasLlenas.Apuntar(reserva.Id, ahora, plazo).ConfigureAwait(false);
                        marcas[reserva.Id] = new MarcaPlazasLlenas { Id = reserva.Id, AvisoPlazasLlenas = ahora, PlazoPagoPlazasLlenas = plazo };
                        resultado.Avisos.Add(new AvisoPlazasLlenasInscripcion { Evento = evento, Inscripcion = reserva, Plazo = plazo });
                    }
                }

                // 3. El próximo plazo que vence (para programar la siguiente pasada justo entonces)
                List<DateTime> pendientes = sinPagar
                    .Where(i => marcas.TryGetValue(i.Id, out MarcaPlazasLlenas m) && m.PlazoPagoPlazasLlenas.HasValue)
                    .Select(i => marcas[i.Id].PlazoPagoPlazasLlenas.Value)
                    .ToList();
                if (pendientes.Any())
                {
                    resultado.ProximosPlazos[evento.Id] = pendientes.Min();
                }
            }
            if (cambios)
            {
                await db.SaveChangesAsync().ConfigureAwait(false);
            }
            await PonerUrlsPago(resultado.Avisos).ConfigureAwait(false);
            return resultado;
        }

        /// <summary>La página del enlace de pago de cada aviso que lo tiene (mientras se puede pagar).</summary>
        private async Task PonerUrlsPago(List<AvisoPlazasLlenasInscripcion> avisos)
        {
            List<int> pagos = avisos.Where(a => a.Inscripcion.PagoTPVId.HasValue).Select(a => a.Inscripcion.PagoTPVId.Value).Distinct().ToList();
            if (!pagos.Any())
            {
                return;
            }
            Dictionary<int, Guid> tokens = (await db.PagosTPV.Where(p => pagos.Contains(p.Id))
                .Select(p => new { p.Id, p.TokenAcceso }).ToListAsync().ConfigureAwait(false))
                .GroupBy(p => p.Id).ToDictionary(g => g.Key, g => g.First().TokenAcceso);
            foreach (AvisoPlazasLlenasInscripcion aviso in avisos.Where(a => a.Inscripcion.PagoTPVId.HasValue))
            {
                if (tokens.TryGetValue(aviso.Inscripcion.PagoTPVId.Value, out Guid token))
                {
                    aviso.UrlPago = URL_PAGINA_PAGO + token;
                }
            }
        }

        /// <summary>
        /// El rechazo de una inscripción nueva sin pagar con el evento lleno: cuántas son reservas sin pagar y cuándo vence la
        /// primera (el plazo del aviso si lo tiene; si no, su último día para pagar).
        /// </summary>
        private async Task<string> MensajeSinPlazas(Evento evento, List<EventoInscripcion> delEvento)
        {
            List<EventoInscripcion> sinPagar = delEvento.Where(ReglasPlazasLlenas.EsReservaSinPagar).ToList();
            Dictionary<int, MarcaPlazasLlenas> marcas = new Dictionary<int, MarcaPlazasLlenas>();
            if (sinPagar.Any() && PlazasLlenas != null)
            {
                try
                {
                    marcas = await PlazasLlenas.Leer(sinPagar.Select(i => i.Id).ToList()).ConfigureAwait(false) ?? marcas;
                }
                catch (Exception ex)
                {
                    ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se han podido leer los plazos del evento {evento.Id}: {ex.Message}", ex));
                }
            }
            List<VencimientoReservaEvento> vencimientos = sinPagar
                .Select(i => ReglasPlazasLlenas.Vencimiento(i, evento,
                    marcas.TryGetValue(i.Id, out MarcaPlazasLlenas m) ? m.PlazoPagoPlazasLlenas : null))
                .ToList();
            return ReglasPlazasLlenas.MensajeSinPlazas(evento, vencimientos);
        }

        /// <summary>
        /// Tras entrar o recuperar una plaza: si el evento ha quedado lleno y hay reservas sin pagar, encola la pasada de la
        /// política para ese evento (el aviso sale enseguida, no al día siguiente). Nunca falla la operación que lo llama.
        /// </summary>
        private async Task AvisarSiSeLlena(Evento evento)
        {
            if (evento?.Plazas == null || EncolarPlazasLlenas == null)
            {
                return;
            }
            try
            {
                List<EventoInscripcion> suyas = await db.EventosInscripciones
                    .Where(i => i.EventoId == evento.Id).ToListAsync().ConfigureAwait(false);
                if (ReglasPlazasLlenas.EstaLleno(evento, suyas.Count(OcupaPlaza)) && suyas.Any(ReglasPlazasLlenas.EsReservaSinPagar))
                {
                    EncolarPlazasLlenas(evento.Id);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido encolar el aviso del evento {evento.Id}: {ex.Message}", ex));
            }
        }

        /// <summary>
        /// Ha llegado dinero de una inscripción Caducada (pasa a Pagada: el dinero manda, aunque el evento quede por encima de
        /// sus plazas) o Anulada (sigue anulada; el cobro está en el extracto). Queda en ELMAH y se avisa a Administración y a
        /// quien la inscribió para que decidan si se mantiene la plaza o se devuelve. Nunca falla el cobro.
        /// </summary>
        private async Task AvisarFueraDePlazo(EventoInscripcion inscripcion, Evento evento, EstadoInscripcionEvento estadoAnterior)
        {
            try
            {
                int ocupadas = (await db.EventosInscripciones.Where(i => i.EventoId == evento.Id).ToListAsync().ConfigureAwait(false))
                    .Count(OcupaPlaza);
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] Pago de la inscripción {inscripcion.Id} (cliente {Limpiar(inscripcion.Cliente)}, " +
                    $"evento «{evento.Titulo?.Trim()}») que estaba {ReglasInscripcionEvento.Texto(estadoAnterior).ToLowerInvariant()}; " +
                    $"plazas ocupadas {ocupadas}{(evento.Plazas.HasValue ? " de " + evento.Plazas.Value : "")}. Se avisa a Administración."));
                if (AvisarPagoFueraDePlazo != null)
                {
                    await AvisarPagoFueraDePlazo(inscripcion, evento, estadoAnterior, ocupadas).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                ElmahHelper.Log(new Exception($"[Plazas llenas de eventos] No se ha podido avisar del pago fuera de plazo de la inscripción {inscripcion.Id}: {ex.Message}", ex));
            }
        }
    }
}
