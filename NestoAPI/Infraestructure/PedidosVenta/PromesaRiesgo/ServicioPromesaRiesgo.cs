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
        /// <summary>De los resueltos, los que ya salen porque ha llegado lo que faltaba (Carlos, 18/11/26).</summary>
        public int YaSalen { get; set; }
        /// <summary>Riesgos avisados por la campana (0 en Sombra).</summary>
        public int AvisadosCampana { get; set; }
        /// <summary>Buenas noticias por la campana (0 en Sombra).</summary>
        public int AvisadosYaSalen { get; set; }
        /// <summary>Filas «en observación» nuevas (todo en Algete y va tarde; sin aviso todavía).</summary>
        public int Observados { get; set; }
        /// <summary>Observados que pasan a aviso: un día laborable completo con todo en Algete y sin salir.</summary>
        public int SiguenSinSalir { get; set; }
        public List<int> PedidosAvisados { get; set; } = new List<int>();
        /// <summary>
        /// Pedido 930942 (19/11/26): solo en <see cref="ServicioPromesaRiesgo.RevisarAntesDelCorreo"/>, los pedidos recalculados sin error.
        /// El correo solo lleva estos (y lo detectado después de empezar la revisión): nunca un porqué guardado horas antes sin recalcular.
        /// </summary>
        public HashSet<int> PedidosRevisados { get; } = new HashSet<int>();
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
        private readonly Func<DateTime, bool> esFestivoAlgete;

        /// <param name="esFestivoAlgete">Fines de semana y festivos de Algete (para «un día laborable completo»). Null: GestorFestivos.</param>
        public ServicioPromesaRiesgo(IRepositorioPromesaRiesgo repositorio, IServicioFechaEntregaAgencia fechas, IAvisadorPromesaRiesgo avisador,
            Func<DateTime> reloj = null, Func<ModoAvisoPromesaRiesgo> modo = null, Action<Exception> registrar = null,
            Func<DateTime, bool> esFestivoAlgete = null)
        {
            this.esFestivoAlgete = esFestivoAlgete ?? (d => Models.RecursosHumanos.GestorFestivos.EsFestivo(d, Models.Constantes.Almacenes.ALGETE));
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
            // Carlos (18/11/26): la causa del evento («Reina mandó 1 de 3 en la reposición 81094») ya no va en la frase del vendedor
            // (qué falta, cuándo llega, cuándo sale); el evento queda en la columna Evento.
            return Recalcular(evento.Empresa, evento.Productos, evento.Descripcion);
        }

        /// <summary>
        /// La pasada nocturna de seguridad: todos los pedidos pendientes con promesa de hoy o futura (o con un riesgo sin
        /// resolver) y, además, resuelve los riesgos de pedidos que ya se han servido. Cubre lo que no pasa por la API
        /// (Nesto viejo: regularizaciones de inventario, albaranes de compra, cambios directos en la BD…).
        /// </summary>
        public Task<ResultadoPromesaRiesgo> PasadaNocturna(string empresa) => Recalcular(empresa, null, EVENTO_NOCTURNO);

        public const string EVENTO_ANTES_DEL_CORREO = "revisión antes del correo resumen";

        /// <summary>
        /// NestoAPI#730 (sugerencia 853, Lidia, 29/10/26): justo antes del correo resumen, se recalculan los riesgos que van a
        /// salir en él (los que aún no han ido en ninguno), para no mandar lo guardado cuando ya no vale: el riesgo se detectó a
        /// las 9:35 («viene de Alcobendas») y a las 14:49 un inventario lo había dejado obsoleto. Con las mismas reglas que un
        /// evento: el que ya cumple se resuelve (y no sale), el que cambia de fecha se actualiza con su porqué nuevo y el que
        /// empeora avisa por la campana. NestoAPI#846 (853): el que sigue con la MISMA fecha también rehace su porqué con el
        /// stock y las reposiciones de ahora (pedido 929477, 28/10/26: detectado «viene de Alcobendas» y a las 15:00 estaba en
        /// Reina, con la misma fecha nueva). Ese porqué ya no lleva la causa del evento que lo detectó (podría no valer ya).
        /// </summary>
        public async Task<ResultadoPromesaRiesgo> RevisarAntesDelCorreo(string empresa)
        {
            var resultado = new ResultadoPromesaRiesgo();
            ModoAvisoPromesaRiesgo modoActual = modo();
            if (modoActual == ModoAvisoPromesaRiesgo.Apagado || !repositorio.TablaExiste())
            {
                return resultado;
            }
            empresa = string.IsNullOrWhiteSpace(empresa) ? Models.Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
            DateTime ahora = reloj();
            List<RiesgoPromesa> sinCorreo = await repositorio.LeerSinCorreo(empresa).ConfigureAwait(false) ?? new List<RiesgoPromesa>();
            var avisar = new List<Aviso>();
            foreach (RiesgoPromesa registrado in sinCorreo.GroupBy(r => r.Pedido).Select(g => g.First()))
            {
                try
                {
                    (DateTime? prometida, bool quedaPendiente) = await repositorio.LeerEstadoPedido(empresa, registrado.Pedido).ConfigureAwait(false);
                    FechaEntregaAgenciaDTO calculo = quedaPendiente ? await fechas.CalcularPedido(empresa, registrado.Pedido).ConfigureAwait(false) : null;
                    resultado.Recalculados++;
                    await Aplicar(empresa, registrado.Pedido, prometida, calculo, quedaPendiente, registrado, EVENTO_ANTES_DEL_CORREO, ahora,
                        resultado, avisar, rehacerMotivo: true).ConfigureAwait(false);
                    _ = resultado.PedidosRevisados.Add(registrado.Pedido);
                }
                catch (Exception ex)
                {
                    Registrar($"no se ha podido revisar el riesgo del pedido {registrado.Pedido} antes del correo resumen", ex);
                }
            }
            await AvisarCampana(modoActual, avisar, resultado).ConfigureAwait(false);
            return resultado;
        }

        private async Task<ResultadoPromesaRiesgo> Recalcular(string empresa, IEnumerable<string> productos, string descripcion)
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
            var avisar = new List<Aviso>();

            foreach (PedidoConPromesa candidato in candidatos.GroupBy(c => c.Pedido).Select(g => g.First()))
            {
                try
                {
                    FechaEntregaAgenciaDTO calculo = await fechas.CalcularPedido(empresa, candidato.Pedido).ConfigureAwait(false);
                    resultado.Recalculados++;
                    RiesgoPromesa registrado = registrados.FirstOrDefault(r => r.Pedido == candidato.Pedido);
                    await Aplicar(empresa, candidato.Pedido, candidato.FechaPrometida, calculo, true, registrado, descripcion, ahora,
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
                        await Aplicar(empresa, registrado.Pedido, prometida, calculo, quedaPendiente, registrado, descripcion, ahora,
                            resultado, avisar).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        Registrar($"no se ha podido revisar el riesgo del pedido {registrado.Pedido}", ex);
                    }
                }
            }

            await AvisarCampana(modoActual, avisar, resultado).ConfigureAwait(false);
            return resultado;
        }

        /// <summary>Lo que hay que avisar por la campana al final de la pasada.</summary>
        private class Aviso
        {
            public RiesgoPromesa Riesgo { get; set; }
            public bool Empeora { get; set; }
            /// <summary>La buena noticia (la fila ya está resuelta: no se marca AvisadoCampana).</summary>
            public bool YaLlego { get; set; }
        }

        private async Task AvisarCampana(ModoAvisoPromesaRiesgo modoActual, List<Aviso> avisar, ResultadoPromesaRiesgo resultado)
        {
            if (modoActual == ModoAvisoPromesaRiesgo.Activo && avisador != null)
            {
                foreach (Aviso pendiente in avisar)
                {
                    RiesgoPromesa riesgo = pendiente.Riesgo;
                    try
                    {
                        AvisoPromesaRiesgo aviso = avisador.Preparar(riesgo, pendiente.Empeora);
                        aviso.YaLlego = pendiente.YaLlego;
                        if (await avisador.AvisarCampana(aviso).ConfigureAwait(false) > 0)
                        {
                            if (pendiente.YaLlego)
                            {
                                resultado.AvisadosYaSalen++;
                            }
                            else
                            {
                                await repositorio.MarcarAvisadoCampana(riesgo.Id, reloj()).ConfigureAwait(false);
                                resultado.AvisadosCampana++;
                            }
                            resultado.PedidosAvisados.Add(riesgo.Pedido);
                        }
                    }
                    catch (Exception ex)
                    {
                        Registrar($"no se ha podido avisar del pedido {riesgo.Pedido}", ex);
                    }
                }
            }
        }

        private async Task Aplicar(string empresa, int pedido, DateTime? prometida, FechaEntregaAgenciaDTO calculo, bool quedaPendiente,
            RiesgoPromesa registrado, string descripcion, DateTime ahora,
            ResultadoPromesaRiesgo resultado, List<Aviso> avisar, bool rehacerMotivo = false)
        {
            bool vencida = registrado != null && registrado.EnObservacion && registrado.TodoListoDesde.HasValue
                && EvaluadorPromesaRiesgo.ObservacionVencida(registrado.TodoListoDesde.Value, ahora, esFestivoAlgete);
            DecisionPromesaRiesgo decision = EvaluadorPromesaRiesgo.Decidir(prometida, calculo, quedaPendiente, registrado, vencida);
            switch (decision.Accion)
            {
                case AccionPromesaRiesgo.Registrar:
                    avisar.Add(new Aviso { Riesgo = await Registrar(empresa, pedido, prometida.Value, calculo, descripcion, ahora).ConfigureAwait(false) });
                    resultado.Registrados++;
                    break;
                case AccionPromesaRiesgo.Observar:
                    await Observar(empresa, pedido, prometida.Value, calculo, descripcion, ahora, resultado).ConfigureAwait(false);
                    break;
                case AccionPromesaRiesgo.SigueSinSalir:
                    {
                        // Carlos (18/11/26): «si se da ese caso extraño, es bueno que se avise al vendedor». La fila deja de estar en
                        // observación y pasa a ser un aviso normal (listado, campana, correo); TodoListoDesde dice que es de este tipo.
                        registrado.EnObservacion = false;
                        registrado.FechaNueva = calculo.FechaEntregaAgencia?.Date;
                        registrado.Motivo = await TextoSigueSinSalir(empresa, pedido, registrado.TodoListoDesde.Value, calculo, ahora).ConfigureAwait(false);
                        registrado.Evento = descripcion;
                        registrado.Detectado = ahora;
                        registrado.Actualizado = ahora;
                        registrado.AvisadoCampana = null;
                        registrado.AvisadoCorreo = null;
                        await repositorio.Actualizar(registrado).ConfigureAwait(false);
                        avisar.Add(new Aviso { Riesgo = registrado });
                        resultado.SiguenSinSalir++;
                    }
                    break;
                case AccionPromesaRiesgo.Empeora:
                    {
                        Explicacion explicacion = await Explicar(empresa, pedido, registrado.FechaPrometida, calculo, registrado.TodoListoDesde).ConfigureAwait(false);
                        registrado.TodoListoDesde = explicacion.Faltas.Any() ? null : registrado.TodoListoDesde; // con culpables, es un riesgo normal
                        registrado.FechaNueva = calculo.FechaEntregaAgencia?.Date;
                        registrado.Motivo = explicacion.Motivo;
                        registrado.CausaOriginal = CausaOriginalPromesaRiesgo.Unir(registrado.CausaOriginal, explicacion.Faltas);
                        registrado.Evento = descripcion;
                        registrado.Actualizado = ahora;
                        registrado.AvisadoCampana = null;
                        registrado.AvisadoCorreo = null; // vuelve a salir en el siguiente correo resumen
                        await repositorio.Actualizar(registrado).ConfigureAwait(false);
                        avisar.Add(new Aviso { Riesgo = registrado, Empeora = true });
                        resultado.Empeorados++;
                    }
                    break;
                case AccionPromesaRiesgo.Actualizar:
                    {
                        // Igual que estaba: no se toca el texto. Mejor sin llegar a cumplir: se actualiza la fecha y el texto, sin avisar.
                        // Antes del correo (#846), el texto se rehace aunque la fecha siga igual: el correo cuenta lo de ahora. La causa
                        // original no se pierde nunca: se le suma lo que falte ahora y no estaba (Carlos, 18/11/26).
                        DateTime? nueva = calculo.FechaEntregaAgencia?.Date;
                        bool cambiaFecha = nueva != registrado.FechaNueva?.Date;
                        if (!cambiaFecha && !rehacerMotivo && !repositorio.ColumnasNuevasExisten())
                        {
                            break;
                        }
                        Explicacion explicacion = await Explicar(empresa, pedido, registrado.FechaPrometida, calculo, registrado.TodoListoDesde).ConfigureAwait(false);
                        string causa = CausaOriginalPromesaRiesgo.Unir(registrado.CausaOriginal, explicacion.Faltas);
                        bool escribir = !string.Equals(causa, registrado.CausaOriginal, StringComparison.Ordinal);
                        registrado.CausaOriginal = causa;
                        if (registrado.TodoListoDesde.HasValue && explicacion.Faltas.Any())
                        {
                            registrado.TodoListoDesde = null; // el de «sigue sin salir» ahora tiene un culpable: es un riesgo normal
                            registrado.Motivo = explicacion.Motivo;
                            escribir = true;
                        }
                        if (cambiaFecha || (rehacerMotivo && !string.Equals(explicacion.Motivo, registrado.Motivo, StringComparison.Ordinal)))
                        {
                            registrado.FechaNueva = nueva;
                            registrado.Motivo = explicacion.Motivo;
                            escribir = true;
                        }
                        if (escribir)
                        {
                            registrado.Actualizado = ahora;
                            await repositorio.Actualizar(registrado).ConfigureAwait(false);
                        }
                    }
                    break;
                case AccionPromesaRiesgo.YaLlego:
                    {
                        // Carlos (18/11/26): «cuando llegó, decir que ya sale porque acaba de llegar el producto», una vez. La buena
                        // noticia va por la campana solo a quien recibió la mala, y en el correo solo si la mala salió en un correo.
                        bool avisadoCampana = registrado.AvisadoCampana.HasValue;
                        bool avisadoCorreo = registrado.AvisadoCorreo.HasValue;
                        registrado.Motivo = TextosPromesaRiesgo.YaLlego(CausaOriginalPromesaRiesgo.Leer(registrado.CausaOriginal), ahora,
                            calculo.FechaEntregaAgencia, ahora.Date);
                        registrado.FechaNueva = calculo.FechaEntregaAgencia?.Date;
                        registrado.Actualizado = ahora;
                        registrado.AvisadoCorreo = avisadoCorreo ? (DateTime?)null : ahora; // null = la buena noticia sale en el siguiente correo
                        await repositorio.Actualizar(registrado).ConfigureAwait(false);
                        await repositorio.Resolver(registrado.Id, ahora, decision.MotivoResolucion).ConfigureAwait(false);
                        resultado.Resueltos++;
                        resultado.YaSalen++;
                        if (avisadoCampana)
                        {
                            avisar.Add(new Aviso { Riesgo = registrado, YaLlego = true });
                        }
                        // Y se queda en observación: si al día laborable siguiente sigue sin salir, se avisa (caso raro).
                        if (decision.AccionTrasRenegociar == AccionPromesaRiesgo.Observar)
                        {
                            await Observar(empresa, pedido, registrado.FechaPrometida, calculo, descripcion, ahora, resultado).ConfigureAwait(false);
                        }
                    }
                    break;
                case AccionPromesaRiesgo.Resolver:
                    await repositorio.Resolver(registrado.Id, ahora, decision.MotivoResolucion).ConfigureAwait(false);
                    if (!registrado.EnObservacion)
                    {
                        resultado.Resueltos++;
                    }
                    if (decision.AccionTrasRenegociar == AccionPromesaRiesgo.Registrar)
                    {
                        avisar.Add(new Aviso { Riesgo = await Registrar(empresa, pedido, prometida.Value, calculo, descripcion, ahora).ConfigureAwait(false) });
                        resultado.Registrados++;
                    }
                    else if (decision.AccionTrasRenegociar == AccionPromesaRiesgo.Observar)
                    {
                        await Observar(empresa, pedido, prometida.Value, calculo, descripcion, ahora, resultado).ConfigureAwait(false);
                    }
                    break;
            }
        }

        /// <summary>
        /// Fila «en observación»: todo en Algete, va tarde y aún no toca avisar. Sin las columnas nuevas, nada (como antes: sin aviso).
        /// </summary>
        private async Task Observar(string empresa, int pedido, DateTime prometida, FechaEntregaAgenciaDTO calculo, string descripcion, DateTime ahora,
            ResultadoPromesaRiesgo resultado)
        {
            if (!repositorio.ColumnasNuevasExisten())
            {
                return;
            }
            var observado = new RiesgoPromesa
            {
                Empresa = empresa,
                Pedido = pedido,
                FechaPrometida = prometida.Date,
                FechaNueva = calculo.FechaEntregaAgencia?.Date,
                Motivo = TextosPromesaRiesgo.TODO_EN_ALGETE,
                Evento = descripcion,
                Detectado = ahora,
                Actualizado = ahora,
                TodoListoDesde = ahora,
                EnObservacion = true
            };
            observado.Id = await repositorio.Insertar(observado).ConfigureAwait(false);
            resultado.Observados++;
        }

        private async Task<string> TextoSigueSinSalir(string empresa, int pedido, DateTime desde, FechaEntregaAgenciaDTO calculo, DateTime ahora)
        {
            TextosPromesaRiesgo.CausasSinSalir causas;
            try
            {
                causas = await repositorio.LeerCausasSinSalir(empresa, pedido).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Registrar($"no se han podido leer las causas de que el pedido {pedido} no salga", ex);
                causas = null;
            }
            return TextosPromesaRiesgo.SigueSinSalir(desde, calculo, causas, ahora.Date);
        }

        private async Task<RiesgoPromesa> Registrar(string empresa, int pedido, DateTime prometida, FechaEntregaAgenciaDTO calculo,
            string descripcion, DateTime ahora)
        {
            Explicacion explicacion = await Explicar(empresa, pedido, prometida, calculo).ConfigureAwait(false);
            var riesgo = new RiesgoPromesa
            {
                Empresa = empresa,
                Pedido = pedido,
                FechaPrometida = prometida.Date,
                FechaNueva = calculo.FechaEntregaAgencia?.Date,
                Motivo = explicacion.Motivo,
                CausaOriginal = CausaOriginalPromesaRiesgo.Serializar(explicacion.Faltas),
                Evento = descripcion,
                Detectado = ahora,
                Actualizado = ahora
            };
            riesgo.Id = await repositorio.Insertar(riesgo).ConfigureAwait(false);
            return riesgo;
        }

        private class Explicacion
        {
            public string Motivo { get; set; }
            public List<FaltaPromesaRiesgo> Faltas { get; set; }
        }

        /// <summary>
        /// La frase para el vendedor y lo que falta (para la causa original), con los nombres de los culpables. Con
        /// <paramref name="todoListoDesde"/> (el aviso de «sigue sin salir») y sin culpables, la frase de ese caso.
        /// </summary>
        private async Task<Explicacion> Explicar(string empresa, int pedido, DateTime prometida, FechaEntregaAgenciaDTO calculo,
            DateTime? todoListoDesde = null)
        {
            if (todoListoDesde.HasValue && !TextosPromesaRiesgo.Culpables(prometida, calculo).Any())
            {
                return new Explicacion
                {
                    Motivo = await TextoSigueSinSalir(empresa, pedido, todoListoDesde.Value, calculo, reloj()).ConfigureAwait(false),
                    Faltas = new List<FaltaPromesaRiesgo>()
                };
            }
            List<string> culpables = TextosPromesaRiesgo.Culpables(prometida, calculo).Select(p => p.Producto?.Trim())
                .Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Dictionary<string, string> nombres;
            try
            {
                nombres = culpables.Any()
                    ? await repositorio.LeerNombresProductos(empresa, culpables).ConfigureAwait(false) ?? new Dictionary<string, string>()
                    : new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                Registrar($"no se han podido leer los nombres de los productos del pedido {pedido}", ex);
                nombres = new Dictionary<string, string>();
            }
            return new Explicacion
            {
                Motivo = TextosPromesaRiesgo.Motivo(prometida, calculo, nombres, reloj().Date),
                Faltas = TextosPromesaRiesgo.Faltas(prometida, calculo, nombres)
            };
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
