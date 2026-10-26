using Elmah;
using NestoAPI.Infraestructure.ChequesRegalo;
using NestoAPI.Models;
using NestoAPI.Models.RecursosHumanos;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Rapports
{
    public interface IServicioSugerenciasContacto
    {
        Task<SugerenciasContactoDTO> Leer(string vendedor, string tipoInteraccion, int numero, string grupoSubgrupo, string usuario);
        Task<List<UsoSugerenciasContactoDTO>> LeerUso(DateTime desde, DateTime hasta);
    }

    /// <summary>
    /// NestoAPI#603 (corte 1): junta la cartera (<see cref="IRepositorioCarteraContacto"/>), la probabilidad del modelo
    /// (<see cref="IProbabilidadesContacto"/>), el motor (<see cref="MotorSugerenciasContacto"/>) y el registro del día
    /// (<see cref="RegistroSugerenciasContacto"/>).
    /// <para>NestoAPI#677: la lista va por probabilidad con un cupo de cobertura intercalado (<see cref="MotorSugerenciasContacto.ElegirDelDia"/>);
    /// el origen de cada una (probabilidad, cobertura o cheque) queda en el principio del Motivo registrado.</para>
    /// <para>La primera consulta del día de un vendedor fija su lista: se guardan las max(<c>numero</c>, objetivo de hoy, cupo de cobertura) primeras y las consultas
    /// siguientes devuelven esas mismas (con Atendida al día y los datos del cliente frescos). Si piden más de las que
    /// hay, se añaden las siguientes de la lista viva que no estuvieran ya. El ritmo y los pendientes se calculan siempre
    /// en vivo.</para>
    /// </summary>
    public class ServicioSugerenciasContacto : IServicioSugerenciasContacto
    {
        public const int NUMERO_MAXIMO = 200;

        private readonly NVEntities db;
        private readonly IRepositorioCarteraContacto repositorio;
        private readonly IProbabilidadesContacto probabilidades;
        private readonly MotorSugerenciasContacto motor;
        private readonly Func<DateTime> reloj;
        private readonly Func<DateTime, string, bool> esFestivo;
        private readonly IServicioFrasesRitmo frases;
        private readonly IBloqueoSugerenciasDelDia bloqueo;
        private readonly IChequesRegaloParaSugerencias chequesRegalo;

        public ServicioSugerenciasContacto(NVEntities db)
            : this(db, new RepositorioCarteraContactoSql(), new ProbabilidadesContactoModelo(), () => DateTime.Now, GestorFestivos.EsFestivo,
                  new ServicioFrasesRitmo(), null, new ServicioEstadisticaChequesRegalo(new RepositorioEstadisticaChequesRegalo(db)))
        {
        }

        /// <param name="frases">NestoAPI#603 (corte 4): la frase del ritmo. Null = la plantilla fija del corte 1.</param>
        /// <param name="chequesRegalo">NestoAPI#593: los clientes con el cheque sin usar en los últimos días de la campaña
        /// (<see cref="ImpulsoChequesRegaloContacto"/>). Null = sin impulso.</param>
        internal ServicioSugerenciasContacto(NVEntities db, IRepositorioCarteraContacto repositorio, IProbabilidadesContacto probabilidades,
            Func<DateTime> reloj, Func<DateTime, string, bool> esFestivo, IServicioFrasesRitmo frases = null,
            IBloqueoSugerenciasDelDia bloqueo = null, IChequesRegaloParaSugerencias chequesRegalo = null)
        {
            this.chequesRegalo = chequesRegalo;
            this.bloqueo = bloqueo;
            this.frases = frases;
            this.db = db;
            this.repositorio = repositorio;
            this.probabilidades = probabilidades;
            this.reloj = reloj;
            this.esFestivo = esFestivo;
            motor = new MotorSugerenciasContacto();
        }

        public async Task<SugerenciasContactoDTO> Leer(string vendedor, string tipoInteraccion, int numero, string grupoSubgrupo, string usuario)
        {
            if (string.IsNullOrWhiteSpace(vendedor))
            {
                throw new ArgumentException("Hay que indicar el vendedor.", nameof(vendedor));
            }
            string vendedorLimpio = vendedor.Trim().ToUpperInvariant();
            numero = Math.Max(1, Math.Min(NUMERO_MAXIMO, numero));
            DateTime ahora = reloj();
            DateTime hoy = ahora.Date;

            List<ClienteCarteraContacto> cartera = await repositorio.LeerCartera(vendedorLimpio, hoy).ConfigureAwait(false);
            Dictionary<string, PrediccionContacto> predicciones = await probabilidades.Leer(vendedorLimpio, tipoInteraccion, grupoSubgrupo).ConfigureAwait(false)
                ?? new Dictionary<string, PrediccionContacto>();
            foreach (ClienteCarteraContacto cliente in cartera)
            {
                if (predicciones.TryGetValue(cliente.Clave, out PrediccionContacto prediccion))
                {
                    cliente.Probabilidad = prediccion.Probabilidad;
                    cliente.GrupoSubgrupoMasVendido = prediccion.GrupoSubgrupoMasVendido;
                }
            }

            // NestoAPI#593: los del cheque regalo sin usar, delante (regla después del motor; el modelo no se toca).
            Dictionary<string, ClienteConChequeRegalo> conCheque = ImpulsoChequesRegaloContacto.Asignar(cartera,
                await LeerImpulsosChequeRegalo(vendedorLimpio, hoy).ConfigureAwait(false), hoy);
            List<SugerenciaContactoDTO> pendientes = ImpulsoChequesRegaloContacto.Aplicar(motor.Priorizar(cartera, hoy), conCheque, hoy);
            // Sugerencia 782: clientes distintos de la cartera, no rapports.
            ContactosVendedor contactos = MotorSugerenciasContacto.ContarContactos(
                await repositorio.LeerContactos(vendedorLimpio, hoy).ConfigureAwait(false), cartera, hoy);
            string delegacion = await repositorio.LeerDelegacion(vendedorLimpio).ConfigureAwait(false) ?? Constantes.Almacenes.ALGETE;
            RitmoContactosDTO ritmo = motor.CalcularRitmo(cartera, contactos, pendientes, hoy, d => !esFestivo(d, delegacion));
            if (frases != null)
            {
                ritmo.Frase = await frases.Generar(vendedorLimpio, ritmo, ahora, d => !esFestivo(d, delegacion)).ConfigureAwait(false);
            }
            // NestoAPI#677: el último laborable del mes el cupo de cobertura son todos los que falten, aunque pase del objetivo.
            numero = NumeroDelDia(numero, Math.Max(ritmo.ObjetivoHoy, ritmo.CupoCoberturaHoy));

            var registro = new RegistroSugerenciasContacto(db, bloqueo);
            List<SugerenciaContacto> delDia = await registro.LeerDelDia(vendedorLimpio, hoy).ConfigureAwait(false);
            bool cambios = await registro.ConciliarAtendidas(delDia, hoy).ConfigureAwait(false);
            if (delDia.Count < numero)
            {
                // NestoAPI#603: bajo bloqueo por vendedor y día (dos llamadas a la vez no duplican la lista). Dentro se
                // vuelve a leer: si la otra llamada ya la registró, no se añade nada.
                delDia = await registro.CompletarDelDia(vendedorLimpio, hoy, actuales =>
                {
                    if (actuales.Count >= numero)
                    {
                        return new List<SugerenciaContacto>();
                    }
                    // NestoAPI#677: por probabilidad, con el cupo de cobertura del día (menos lo que ya se registró por cupo hoy).
                    int coberturaRegistrada = actuales.Count(s => OrigenesSugerenciaContacto.DeMotivo(s.Motivo) == OrigenesSugerenciaContacto.COBERTURA);
                    List<SugerenciaContactoDTO> nuevas = MotorSugerenciasContacto.ElegirDelDia(pendientes, cartera, hoy,
                        numero - actuales.Count, Math.Max(0, ritmo.CupoCoberturaHoy - coberturaRegistrada),
                        actuales.Select(s => ClienteCarteraContacto.ClaveDe(s.Cliente, s.Contacto)));
                    int ultimoOrden = actuales.Any() ? actuales.Max(s => s.Orden) : 0;
                    return registro.Anadir(nuevas, ultimoOrden, vendedorLimpio, usuario, ahora);
                }).ConfigureAwait(false);
            }
            else if (cambios)
            {
                _ = await registro.Guardar().ConfigureAwait(false);
            }

            Dictionary<string, ClienteCarteraContacto> porClave = cartera
                .GroupBy(c => c.Clave, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            return new SugerenciasContactoDTO
            {
                Vendedor = vendedorLimpio,
                Fecha = ahora,
                Ritmo = ritmo,
                // Lo ya registrado en el día no desaparece aunque el objetivo de hoy baje al ir llamando.
                Sugerencias = Ordenar(delDia
                    .OrderBy(s => s.Orden)
                    .Take(Math.Min(NUMERO_MAXIMO, Math.Max(numero, delDia.Count)))
                    .Select(s => ADTO(s, porClave, conCheque, hoy))
                    .ToList())
            };
        }

        /// <summary>
        /// NestoAPI#617: quién entra en la lista lo fija el registro del día (orden registrado), pero el orden de la
        /// respuesta sigue al filtro de esta llamada, con el mismo criterio que <see cref="MotorSugerenciasContacto.ElegirDelDia"/>
        /// (NestoAPI#677): delante las subidas por el cheque regalo; luego las de probabilidad por la probabilidad de ESTA
        /// llamada, con las del cupo de cobertura intercaladas en su orden registrado. El Orden registrado desempata.
        /// Orden = 1..n en la respuesta (en BD no cambia).
        /// </summary>
        internal static List<SugerenciaContactoDTO> Ordenar(List<SugerenciaContactoDTO> registradas)
        {
            var conPuesto = registradas.Select((s, i) => new { Sugerencia = s, Registrado = i }).ToList();
            List<SugerenciaContactoDTO> delante = conPuesto
                .Where(x => x.Sugerencia.TieneChequeRegalo && x.Sugerencia.Prioridad == PrioridadesContacto.MAXIMA)
                .OrderByDescending(x => x.Sugerencia.Probabilidad)
                .ThenBy(x => x.Registrado)
                .Select(x => x.Sugerencia)
                .ToList();
            var resto = conPuesto.Where(x => !delante.Contains(x.Sugerencia)).ToList();
            List<SugerenciaContactoDTO> porProbabilidad = resto
                .Where(x => x.Sugerencia.Origen != OrigenesSugerenciaContacto.COBERTURA)
                .OrderByDescending(x => x.Sugerencia.Probabilidad)
                .ThenBy(x => x.Registrado)
                .Select(x => x.Sugerencia)
                .ToList();
            List<SugerenciaContactoDTO> porCobertura = resto
                .Where(x => x.Sugerencia.Origen == OrigenesSugerenciaContacto.COBERTURA)
                .OrderBy(x => x.Registrado)
                .Select(x => x.Sugerencia)
                .ToList();
            List<SugerenciaContactoDTO> ordenadas = delante.Concat(MotorSugerenciasContacto.Intercalar(porProbabilidad, porCobertura)).ToList();
            for (int i = 0; i < ordenadas.Count; i++)
            {
                ordenadas[i].Orden = i + 1;
            }
            return ordenadas;
        }

        /// <summary>
        /// NestoAPI#603: la lista del día tiene al menos el objetivo de llamadas de hoy (Nesto pide 20 fijo), sin pasar de
        /// <see cref="NUMERO_MAXIMO"/>. Si el objetivo sube después de registrar la lista, la siguiente consulta la completa.
        /// </summary>
        internal static int NumeroDelDia(int numeroPedido, int objetivoHoy)
        {
            return Math.Min(NUMERO_MAXIMO, Math.Max(numeroPedido, objetivoHoy));
        }

        /// <summary>
        /// NestoAPI#593: los clientes con el cheque sin usar, si la campaña está en sus últimos días. Si falla, la lista sale
        /// igual, sin impulso (y queda en ELMAH).
        /// </summary>
        private async Task<List<ChequeRegaloImpulso>> LeerImpulsosChequeRegalo(string vendedor, DateTime hoy)
        {
            if (chequesRegalo == null)
            {
                return new List<ChequeRegaloImpulso>();
            }
            try
            {
                return await chequesRegalo.LeerImpulsos(vendedor, hoy).ConfigureAwait(false) ?? new List<ChequeRegaloImpulso>();
            }
            catch (Exception ex)
            {
                ErrorLog.GetDefault(null)?.Log(new Error(new Exception(
                    $"NestoAPI#593: no se han podido leer los cheques regalo para las sugerencias de {vendedor}; la lista sale sin impulso", ex)));
                return new List<ChequeRegaloImpulso>();
            }
        }

        private static SugerenciaContactoDTO ADTO(SugerenciaContacto fila, Dictionary<string, ClienteCarteraContacto> cartera,
            Dictionary<string, ClienteConChequeRegalo> conCheque, DateTime hoy)
        {
            bool enCartera = cartera.TryGetValue(ClienteCarteraContacto.ClaveDe(fila.Cliente, fila.Contacto), out ClienteCarteraContacto cliente);
            SugerenciaContactoDTO dto = enCartera
                ? MotorSugerenciasContacto.ADTO(cliente, hoy)
                : new SugerenciaContactoDTO { Cliente = fila.Cliente?.Trim(), Contacto = fila.Contacto?.Trim(), DiasDesdeUltimoPedido = 9999 };
            dto.SugerenciaId = fila.Id;
            dto.Prioridad = fila.Prioridad?.Trim();
            dto.Orden = fila.Orden;
            dto.Atendida = fila.Atendida;
            dto.Origen = OrigenesSugerenciaContacto.DeMotivo(fila.Motivo);
            // NestoAPI#617: la lista del día (quién y con qué prioridad) es fija, pero la probabilidad (y el motivo, que en
            // Máxima la lleva) siguen al tipo de interacción y al subgrupo de ESTA llamada: la cartera ya trae la predicción
            // del filtro actual (y ADTO la ha copiado). Lo guardado era la del filtro con que se registró. Si el cliente ya no
            // está en la cartera, se queda lo guardado.
            if (enCartera && conCheque != null && conCheque.TryGetValue(cliente.Clave, out ClienteConChequeRegalo con))
            {
                // NestoAPI#593: con el cheque sin usar, la frase del cheque delante; si sube por el cheque, en Máxima
                // aunque se registrara antes con otra prioridad.
                dto.TieneChequeRegalo = true;
                dto.Motivo = ImpulsoChequesRegaloContacto.Motivo(con, hoy);
                if (!con.Excluido)
                {
                    dto.Prioridad = PrioridadesContacto.MAXIMA;
                }
            }
            else if (enCartera && dto.Origen == OrigenesSugerenciaContacto.COBERTURA)
            {
                dto.Motivo = MotorSugerenciasContacto.MotivoCobertura(cliente, hoy);
            }
            else if (enCartera)
            {
                dto.Motivo = MotorSugerenciasContacto.Motivo(cliente, dto.Prioridad, hoy);
            }
            else
            {
                dto.Motivo = fila.Motivo;
                dto.Probabilidad = fila.Probabilidad;
            }
            return dto;
        }

        public async Task<List<UsoSugerenciasContactoDTO>> LeerUso(DateTime desde, DateTime hasta)
        {
            DateTime inicio = desde.Date;
            DateTime fin = hasta.Date.AddDays(1);
            var filas = await db.SugerenciasContacto
                .Where(s => s.Fecha >= inicio && s.Fecha < fin)
                .Select(s => new { s.Vendedor, s.Fecha, s.Atendida, s.Motivo })
                .ToListAsync().ConfigureAwait(false);
            if (!filas.Any())
            {
                return new List<UsoSugerenciasContactoDTO>();
            }
            List<string> vendedores = filas.Select(f => f.Vendedor.Trim()).Distinct().ToList();
            var rapports = await db.SeguimientosClientes
                .Where(r => r.Fecha >= inicio && r.Fecha < fin && (r.Estado == 0 || r.Estado == 1) && vendedores.Contains(r.Vendedor))
                .GroupBy(r => r.Vendedor)
                .Select(g => new { Vendedor = g.Key, Total = g.Count() })
                .ToListAsync().ConfigureAwait(false);
            Dictionary<string, int> rapportsPorVendedor = rapports
                .GroupBy(r => r.Vendedor.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.Total), StringComparer.OrdinalIgnoreCase);

            return filas
                .GroupBy(f => f.Vendedor.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    int sugeridas = g.Count();
                    int atendidas = g.Count(f => f.Atendida);
                    return new UsoSugerenciasContactoDTO
                    {
                        Vendedor = g.Key,
                        DiasConSugerencias = g.Select(f => f.Fecha.Date).Distinct().Count(),
                        Sugeridas = sugeridas,
                        Atendidas = atendidas,
                        PorcentajeAtendidas = sugeridas == 0 ? 0 : Math.Round((double)atendidas / sugeridas, 4),
                        RapportsTotales = rapportsPorVendedor.TryGetValue(g.Key, out int total) ? total : 0,
                        // NestoAPI#677: para comparar la conversión de lo que entra por probabilidad y por el cupo de cobertura.
                        PorOrigen = g
                            .GroupBy(f => OrigenesSugerenciaContacto.DeMotivo(f.Motivo))
                            .Select(o => new UsoOrigenSugerenciasContactoDTO
                            {
                                Origen = o.Key,
                                Sugeridas = o.Count(),
                                Atendidas = o.Count(f => f.Atendida),
                                PorcentajeAtendidas = Math.Round((double)o.Count(f => f.Atendida) / o.Count(), 4)
                            })
                            .OrderBy(o => o.Origen, StringComparer.Ordinal)
                            .ToList()
                    };
                })
                .OrderBy(u => u.Vendedor)
                .ToList();
        }
    }
}
