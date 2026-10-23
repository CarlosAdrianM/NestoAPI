using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.PedidosVenta
{
    /// <summary>NestoAPI#693: lo que ha hecho una pasada (evento o nocturna).</summary>
    public class ResultadoPromesaRiesgo
    {
        public int Recalculados { get; set; }
        public int Registrados { get; set; }
        public int Empeorados { get; set; }
        public int Resueltos { get; set; }
        /// <summary>Riesgos avisados por la campana (0 en Sombra).</summary>
        public int AvisadosCampana { get; set; }
        public List<int> PedidosAvisados { get; set; } = new List<int>();
    }

    /// <summary>
    /// NestoAPI#693: recalcula con el MISMO motor que dio la fecha prometida (<see cref="IServicioFechaEntregaAgencia.CalcularPedido"/>,
    /// #606) solo los pedidos afectados por un evento (pendientes, con promesa de hoy o futura, que llevan los productos tocados),
    /// registra en PedidosPromesaRiesgo los que se retrasan o se quedan sin fecha, resuelve los que vuelven a cumplir y, en
    /// modo Activo, avisa por la campana a los nuevos y a los que empeoran. El correo resumen lo manda otro job
    /// (<see cref="PromesaRiesgoJobsService.EnviarCorreoResumen"/>), nunca uno por movimiento.
    /// </summary>
    public class ServicioPromesaRiesgo
    {
        public const string EVENTO_NOCTURNO = "pasada nocturna";

        private readonly IRepositorioPromesaRiesgo repositorio;
        private readonly IServicioFechaEntregaAgencia fechas;
        private readonly IAvisadorPromesaRiesgo avisador;
        private readonly Func<DateTime> reloj;
        private readonly Func<ModoAvisoPromesaRiesgo> modo;
        private readonly Action<Exception> registrar;

        public ServicioPromesaRiesgo(IRepositorioPromesaRiesgo repositorio, IServicioFechaEntregaAgencia fechas, IAvisadorPromesaRiesgo avisador,
            Func<DateTime> reloj = null, Func<ModoAvisoPromesaRiesgo> modo = null, Action<Exception> registrar = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.fechas = fechas ?? throw new ArgumentNullException(nameof(fechas));
            this.avisador = avisador;
            this.reloj = reloj ?? (() => DateTime.Now);
            this.modo = modo ?? (() => InterruptorAvisoPromesaRiesgo.Modo);
            this.registrar = registrar ?? (ex => ElmahHelper.Log(ex, "Sistema (promesa en riesgo)"));
        }

        /// <summary>Un evento: solo los pedidos que llevan sus productos.</summary>
        public Task<ResultadoPromesaRiesgo> ProcesarEvento(EventoPromesaRiesgo evento)
        {
            if (evento == null || evento.Productos == null || !evento.Productos.Any(p => !string.IsNullOrWhiteSpace(p)))
            {
                return Task.FromResult(new ResultadoPromesaRiesgo());
            }
            // Hangfire deserializa el diccionario sin el comparador: se rehace sin distinguir mayúsculas.
            var causas = new Dictionary<string, CausaPromesaRiesgo>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, CausaPromesaRiesgo> causa in evento.Causas ?? new Dictionary<string, CausaPromesaRiesgo>())
            {
                if (!string.IsNullOrWhiteSpace(causa.Key) && causa.Value != null)
                {
                    causas[causa.Key.Trim()] = causa.Value;
                }
            }
            return Recalcular(evento.Empresa, evento.Productos, causas, evento.Descripcion);
        }

        /// <summary>
        /// La pasada nocturna de seguridad: todos los pedidos pendientes con promesa de hoy o futura (o con un riesgo sin
        /// resolver) y, además, resuelve los riesgos de pedidos que ya se han servido. Cubre lo que no pasa por la API
        /// (Nesto viejo: regularizaciones de inventario, albaranes de compra, cambios directos en la BD…).
        /// </summary>
        public Task<ResultadoPromesaRiesgo> PasadaNocturna(string empresa) => Recalcular(empresa, null, null, EVENTO_NOCTURNO);

        private async Task<ResultadoPromesaRiesgo> Recalcular(string empresa, IEnumerable<string> productos,
            IDictionary<string, CausaPromesaRiesgo> causas, string descripcion)
        {
            var resultado = new ResultadoPromesaRiesgo();
            ModoAvisoPromesaRiesgo modoActual = modo();
            if (modoActual == ModoAvisoPromesaRiesgo.Apagado || !repositorio.TablaExiste())
            {
                return resultado;
            }
            empresa = string.IsNullOrWhiteSpace(empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            bool nocturna = productos == null;
            DateTime ahora = reloj();

            List<PedidoConPromesa> candidatos = await repositorio.LeerCandidatos(empresa, productos, ahora.Date).ConfigureAwait(false)
                ?? new List<PedidoConPromesa>();
            List<RiesgoPromesa> registrados = await repositorio.LeerSinResolver(empresa, nocturna ? null : candidatos.Select(c => c.Pedido))
                .ConfigureAwait(false) ?? new List<RiesgoPromesa>();
            var avisar = new List<(RiesgoPromesa riesgo, bool empeora)>();

            foreach (PedidoConPromesa candidato in candidatos.GroupBy(c => c.Pedido).Select(g => g.First()))
            {
                try
                {
                    FechaEntregaAgenciaDTO calculo = await fechas.CalcularPedido(empresa, candidato.Pedido).ConfigureAwait(false);
                    resultado.Recalculados++;
                    RiesgoPromesa registrado = registrados.FirstOrDefault(r => r.Pedido == candidato.Pedido);
                    await Aplicar(empresa, candidato.Pedido, candidato.FechaPrometida, calculo, true, registrado, causas, descripcion, ahora,
                        resultado, avisar).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Registrar($"no se ha podido recalcular el pedido {candidato.Pedido} ({descripcion})", ex);
                }
            }

            if (nocturna)
            {
                // Riesgos de pedidos que ya no son candidatos: servidos, borrados o sin prometida.
                foreach (RiesgoPromesa registrado in registrados.Where(r => !candidatos.Any(c => c.Pedido == r.Pedido)))
                {
                    try
                    {
                        (DateTime? prometida, bool quedaPendiente) = await repositorio.LeerEstadoPedido(empresa, registrado.Pedido).ConfigureAwait(false);
                        FechaEntregaAgenciaDTO calculo = quedaPendiente ? await fechas.CalcularPedido(empresa, registrado.Pedido).ConfigureAwait(false) : null;
                        await Aplicar(empresa, registrado.Pedido, prometida, calculo, quedaPendiente, registrado, causas, descripcion, ahora,
                            resultado, avisar).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Registrar($"no se ha podido revisar el riesgo del pedido {registrado.Pedido}", ex);
                    }
                }
            }

            if (modoActual == ModoAvisoPromesaRiesgo.Activo && avisador != null)
            {
                foreach ((RiesgoPromesa riesgo, bool empeora) in avisar)
                {
                    try
                    {
                        AvisoPromesaRiesgo aviso = avisador.Preparar(riesgo, empeora);
                        if (await avisador.AvisarCampana(aviso).ConfigureAwait(false) > 0)
                        {
                            await repositorio.MarcarAvisadoCampana(riesgo.Id, reloj()).ConfigureAwait(false);
                            resultado.AvisadosCampana++;
                            resultado.PedidosAvisados.Add(riesgo.Pedido);
                        }
                    }
                    catch (Exception ex)
                    {
                        Registrar($"no se ha podido avisar del pedido {riesgo.Pedido}", ex);
                    }
                }
            }
            return resultado;
        }

        private async Task Aplicar(string empresa, int pedido, DateTime? prometida, FechaEntregaAgenciaDTO calculo, bool quedaPendiente,
            RiesgoPromesa registrado, IDictionary<string, CausaPromesaRiesgo> causas, string descripcion, DateTime ahora,
            ResultadoPromesaRiesgo resultado, List<(RiesgoPromesa, bool)> avisar)
        {
            DecisionPromesaRiesgo decision = EvaluadorPromesaRiesgo.Decidir(prometida, calculo, quedaPendiente, registrado);
            switch (decision.Accion)
            {
                case AccionPromesaRiesgo.Registrar:
                    avisar.Add((await Registrar(empresa, pedido, prometida.Value, calculo, causas, descripcion, ahora).ConfigureAwait(false), false));
                    resultado.Registrados++;
                    break;
                case AccionPromesaRiesgo.Empeora:
                    registrado.FechaNueva = calculo.FechaEntregaAgencia?.Date;
                    registrado.Motivo = await Motivo(empresa, pedido, registrado.FechaPrometida, calculo, causas).ConfigureAwait(false);
                    registrado.Evento = descripcion;
                    registrado.Actualizado = ahora;
                    registrado.AvisadoCampana = null;
                    registrado.AvisadoCorreo = null; // vuelve a salir en el siguiente correo resumen
                    await repositorio.Actualizar(registrado).ConfigureAwait(false);
                    avisar.Add((registrado, true));
                    resultado.Empeorados++;
                    break;
                case AccionPromesaRiesgo.Actualizar:
                    // Igual que estaba: no se toca (se conserva el porqué con la causa del evento que lo detectó). Mejor sin
                    // llegar a cumplir: se actualiza la fecha y el porqué, sin avisar.
                    DateTime? nueva = calculo.FechaEntregaAgencia?.Date;
                    if (nueva != registrado.FechaNueva?.Date)
                    {
                        registrado.FechaNueva = nueva;
                        registrado.Motivo = await Motivo(empresa, pedido, registrado.FechaPrometida, calculo, causas).ConfigureAwait(false);
                        registrado.Actualizado = ahora;
                        await repositorio.Actualizar(registrado).ConfigureAwait(false);
                    }
                    break;
                case AccionPromesaRiesgo.Resolver:
                    await repositorio.Resolver(registrado.Id, ahora, decision.MotivoResolucion).ConfigureAwait(false);
                    resultado.Resueltos++;
                    if (decision.AccionTrasRenegociar == AccionPromesaRiesgo.Registrar)
                    {
                        avisar.Add((await Registrar(empresa, pedido, prometida.Value, calculo, causas, descripcion, ahora).ConfigureAwait(false), false));
                        resultado.Registrados++;
                    }
                    break;
            }
        }

        private async Task<RiesgoPromesa> Registrar(string empresa, int pedido, DateTime prometida, FechaEntregaAgenciaDTO calculo,
            IDictionary<string, CausaPromesaRiesgo> causas, string descripcion, DateTime ahora)
        {
            var riesgo = new RiesgoPromesa
            {
                Empresa = empresa,
                Pedido = pedido,
                FechaPrometida = prometida.Date,
                FechaNueva = calculo.FechaEntregaAgencia?.Date,
                Motivo = await Motivo(empresa, pedido, prometida, calculo, causas).ConfigureAwait(false),
                Evento = descripcion,
                Detectado = ahora,
                Actualizado = ahora
            };
            riesgo.Id = await repositorio.Insertar(riesgo).ConfigureAwait(false);
            return riesgo;
        }

        private async Task<string> Motivo(string empresa, int pedido, DateTime prometida, FechaEntregaAgenciaDTO calculo,
            IDictionary<string, CausaPromesaRiesgo> causas)
        {
            // Lo que necesita el pedido de cada producto: lo que el motor reparte (todas sus partidas).
            Dictionary<string, int> necesidades = (calculo.Desglose ?? new List<PartidaFechaEntregaAgenciaDTO>())
                .Where(p => !string.IsNullOrWhiteSpace(p.Producto))
                .GroupBy(p => p.Producto.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Sum(p => p.Unidades), StringComparer.OrdinalIgnoreCase);
            List<string> fallan = TextosPromesaRiesgo.PartidasQueFallan(prometida, calculo).Select(p => p.Producto?.Trim()).Distinct().ToList();
            Dictionary<string, string> nombres;
            try
            {
                nombres = fallan.Any()
                    ? await repositorio.LeerNombresProductos(empresa, fallan).ConfigureAwait(false)
                    : new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Registrar($"no se han podido leer los nombres de los productos del pedido {pedido}", ex);
                nombres = new Dictionary<string, string>();
            }
            return TextosPromesaRiesgo.Motivo(pedido, prometida, calculo, causas, necesidades, nombres, reloj().Date);
        }

        private void Registrar(string que, Exception ex)
        {
            try
            {
                registrar(new Exception($"[Promesa en riesgo #693] {que}: {ex.Message}", ex));
            }
            catch
            {
                // nunca romper la pasada
            }
        }
    }
}
