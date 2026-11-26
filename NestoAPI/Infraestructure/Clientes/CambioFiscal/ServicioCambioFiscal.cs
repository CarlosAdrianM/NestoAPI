using NestoAPI.Infraestructure.PedidosVenta;
using NestoAPI.Models;
using NestoAPI.Models.Clientes;
using NestoAPI.Models.PedidosVenta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Clientes.CambioFiscal
{
    /// <summary>NestoAPI#876: cómo ha ido (el controller lo traduce a HTTP: 200, 400, 403, 404, 409 o 503).</summary>
    public class ResultadoCambioFiscal<T>
    {
        public int Estado { get; set; } = 200;
        public string Motivo { get; set; }
        /// <summary>Con el 409: NO_ES_CAMBIO_FISCAL (no son el mismo cliente con otra forma fiscal) u OCUPADO (otro copiando a la vez).</summary>
        public string Codigo { get; set; }
        public T Datos { get; set; }

        public const string NO_ES_CAMBIO_FISCAL = "NO_ES_CAMBIO_FISCAL";
        public const string OCUPADO = "OCUPADO";

        public static ResultadoCambioFiscal<T> Error(int estado, string motivo) =>
            new ResultadoCambioFiscal<T> { Estado = estado, Motivo = motivo, Codigo = estado == 409 ? NO_ES_CAMBIO_FISCAL : null };
        public static ResultadoCambioFiscal<T> Ok(T datos) => new ResultadoCambioFiscal<T> { Datos = datos };
    }

    /// <summary>
    /// NestoAPI#876 (corte nuevo, Carlos 26/11/26): un cliente que cambia de forma fiscal (otro NIF o razón social) se da de alta
    /// con número nuevo. Aquí: ver qué se copiaría de la ficha anterior (previa), copiarlo por bloques en UNA transacción y pasar
    /// sus pedidos pendientes al nuevo con el CambiarCliente de siempre. LA REGLA está en <see cref="ReglaCopiaCambioFiscal"/>.
    /// </summary>
    public class ServicioCambioFiscal
    {
        public const string MENSAJE_SIN_TABLA = "Falta la tabla dbo.ClientesAnteriores (Scripts/Issue876_ClientesAnteriores.sql): " +
            "no se puede saber qué cliente es el anterior de cuál.";

        private readonly IRepositorioCambioFiscal repositorio;

        public ServicioCambioFiscal(IRepositorioCambioFiscal repositorio)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
        }

        internal Func<DateTime> Hoy { get; set; } = () => DateTime.Today;

        internal Action<Exception> RegistrarEnElmah { get; set; } = ElmahHelper.Log;

        /// <summary>
        /// Solo lectura: por bloque, lo que tiene cada uno y lo que cambiaría, y los pedidos pendientes del anterior.
        /// </summary>
        /// <param name="motivoSinPermiso">vendedor del anterior → null si puede o el porqué.</param>
        /// <param name="leerPedidosPendientes">Los pedidos pendientes del anterior.</param>
        public async Task<ResultadoCambioFiscal<CopiaCambioFiscalDTO>> Previa(string empresa, string anterior, string nuevo,
            Func<string, Task<string>> motivoSinPermiso, Func<Task<List<PedidoPendienteCambioFiscalDTO>>> leerPedidosPendientes)
        {
            empresa = Empresa(empresa);
            anterior = anterior?.Trim();
            nuevo = nuevo?.Trim();
            bool hayTabla = await repositorio.TablaExiste().ConfigureAwait(false);
            DatosCambioFiscal datos = await repositorio.Leer(empresa, anterior, nuevo).ConfigureAwait(false);
            DecisionEnlaceCambioFiscal enlace = Enlace(datos, anterior, nuevo);
            if (enlace.Estado == 400 || enlace.Estado == 404)
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(enlace.Estado, enlace.Motivo);
            }
            string sinPermiso = await motivoSinPermiso(datos.Anterior.Principal?.Vendedor).ConfigureAwait(false);
            if (sinPermiso != null)
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(403, sinPermiso);
            }
            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(datos.Anterior, datos.Nuevo, BloquesCambioFiscal.TODOS, Hoy());
            CopiaCambioFiscalDTO dto = Respuesta(empresa, datos, plan, enlace, ensayo: true);
            if (!hayTabla)
            {
                dto.MotivoNoSePuedeCopiar = MENSAJE_SIN_TABLA;
                dto.SeEnlazaAlCopiar = false;
            }
            dto.PedidosPendientes = await leerPedidosPendientes().ConfigureAwait(false);
            dto.Mensaje = plan.HayCambios
                ? $"Se copiarían {plan.Bloques.Count(b => b.HayCambios)} bloques del cliente {anterior} al {nuevo}."
                : $"El cliente {nuevo} ya tiene las condiciones del {anterior}.";
            return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Ok(dto);
        }

        /// <summary>
        /// Copia los bloques pedidos en UNA transacción (o, en ensayo, dice qué copiaría). Idempotente: una segunda vez no hace nada.
        /// </summary>
        /// <param name="publicar">(empresa, cliente) → publica el cliente a Odoo, como cualquier modificación de ficha.</param>
        public async Task<ResultadoCambioFiscal<CopiaCambioFiscalDTO>> Copiar(CopiaCambioFiscalRequest peticion, bool ensayo, string usuario,
            Func<string, Task<string>> motivoSinPermiso, Func<string, string, Task> publicar)
        {
            if (peticion == null)
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(400, "Faltan los datos: empresa, cliente anterior y cliente nuevo.");
            }
            List<string> bloques = BloquesCambioFiscal.Normalizar(peticion.Bloques, out List<string> desconocidos);
            if (desconocidos.Any())
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(400, $"No existe el bloque {string.Join(", ", desconocidos)}. " +
                    $"Los bloques son: {string.Join(", ", BloquesCambioFiscal.TODOS)}.");
            }
            if (!await repositorio.TablaExiste().ConfigureAwait(false))
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(503, MENSAJE_SIN_TABLA);
            }
            string empresa = Empresa(peticion.Empresa);
            string anterior = peticion.ClienteAnterior?.Trim();
            string nuevo = peticion.ClienteNuevo?.Trim();

            // Primero, sin bloquear nada: si no vale o no tiene permiso, se dice sin abrir transacción
            DatosCambioFiscal previos = await repositorio.Leer(empresa, anterior, nuevo).ConfigureAwait(false);
            DecisionEnlaceCambioFiscal enlacePrevio = Enlace(previos, anterior, nuevo);
            if (!enlacePrevio.Vale)
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(enlacePrevio.Estado, enlacePrevio.Motivo);
            }
            string sinPermiso = await motivoSinPermiso(previos.Anterior.Principal?.Vendedor).ConfigureAwait(false);
            if (sinPermiso != null)
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(403, sinPermiso);
            }
            if (ensayo)
            {
                PlanCopiaCambioFiscal planEnsayo = ReglaCopiaCambioFiscal.Planificar(previos.Anterior, previos.Nuevo, bloques, Hoy());
                CopiaCambioFiscalDTO dtoEnsayo = Respuesta(empresa, previos, planEnsayo, enlacePrevio, ensayo: true);
                dtoEnsayo.Mensaje = planEnsayo.HayCambios ? "Ensayo: no se ha copiado nada." : $"El cliente {nuevo} ya tiene todo eso del {anterior}.";
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Ok(dtoEnsayo);
            }

            DatosCambioFiscal leidos = null;
            DecisionCopiaCambioFiscal decision;
            try
            {
                decision = await repositorio.Copiar(empresa, anterior, nuevo, datos =>
                {
                    leidos = datos;
                    return Decidir(empresa, anterior, nuevo, datos, bloques, usuario);
                }, usuario).ConfigureAwait(false);
            }
            catch (TimeoutException ex)
            {
                ResultadoCambioFiscal<CopiaCambioFiscalDTO> ocupado = ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(409, ex.Message);
                ocupado.Codigo = ResultadoCambioFiscal<CopiaCambioFiscalDTO>.OCUPADO;
                return ocupado;
            }
            if (decision?.Enlace == null || !decision.Enlace.Vale)
            {
                return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Error(decision?.Enlace?.Estado ?? 409, decision?.Enlace?.Motivo ?? "No se ha podido copiar.");
            }

            CopiaCambioFiscalDTO dto = Respuesta(empresa, leidos, decision.Plan, decision.Enlace, ensayo: false);
            if (decision.Plan.CambiaFicha)
            {
                try
                {
                    await publicar(empresa, nuevo).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    RegistrarEnElmah(new Exception($"[Cambio de forma fiscal #876] {empresa}/{nuevo}: copiado, pero no se ha podido publicar a Odoo: {ex.Message}", ex));
                    dto.Avisos.Add("Se ha copiado, pero no se ha podido mandar la ficha a Odoo: se mandará en la próxima modificación del cliente.");
                }
            }
            dto.Mensaje = decision.Plan.HayCambios
                ? $"Copiado del cliente {anterior} al {nuevo}: {string.Join(", ", decision.Plan.Bloques.Where(b => b.HayCambios).Select(b => b.Nombre.ToLowerInvariant()))}."
                : $"El cliente {nuevo} ya tenía todo eso del {anterior}: no se ha cambiado nada.";
            return ResultadoCambioFiscal<CopiaCambioFiscalDTO>.Ok(dto);
        }

        /// <summary>Lo que se decide dentro de la transacción, con lo leído bajo el bloqueo (pura salvo la hora).</summary>
        internal DecisionCopiaCambioFiscal Decidir(string empresa, string anterior, string nuevo, DatosCambioFiscal datos, List<string> bloques, string usuario)
        {
            DecisionEnlaceCambioFiscal enlace = Enlace(datos, anterior, nuevo);
            if (!enlace.Vale)
            {
                return new DecisionCopiaCambioFiscal { Enlace = enlace, Escribir = false };
            }
            PlanCopiaCambioFiscal plan = ReglaCopiaCambioFiscal.Planificar(datos.Anterior, datos.Nuevo, bloques, Hoy());
            return new DecisionCopiaCambioFiscal
            {
                Enlace = enlace,
                Plan = plan,
                CrearEnlace = enlace.CrearEnlace,
                Escribir = plan.HayCambios || enlace.CrearEnlace,
                Rastro = new
                {
                    Empresa = empresa,
                    ClienteAnterior = anterior,
                    ClienteNuevo = nuevo,
                    Bloques = bloques,
                    EnlaceCreado = enlace.CrearEnlace,
                    Copiado = plan.Bloques.Where(b => b.HayCambios).Select(b => new { b.Bloque, b.Cambios }),
                    Usuario = usuario
                }
            };
        }

        /// <summary>
        /// Pasa los pedidos al cliente nuevo, uno a uno, por el CambiarCliente de siempre (que recalcula «como si naciera para él»;
        /// con picking, solo si no cambia nada). En ensayo, dice qué cambiaría en cada uno.
        /// </summary>
        /// <param name="leerPedido">número → (cliente, contacto) del pedido, o null si no existe.</param>
        /// <param name="cambiarCliente">(número, petición) → el CambiarCliente de PedidosVenta (cada uno con su contexto).</param>
        public async Task<ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO>> PasarPedidos(PasarPedidosCambioFiscalRequest peticion, bool ensayo, string usuario,
            Func<string, Task<string>> motivoSinPermiso, Func<int, Task<Tuple<string, string>>> leerPedido,
            Func<int, CambiarClientePedidoRequest, Task<ResultadoCambioClientePedido>> cambiarCliente)
        {
            if (peticion == null || peticion.Pedidos == null || !peticion.Pedidos.Any())
            {
                return ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO>.Error(400, "Diga qué pedidos pasan al cliente nuevo.");
            }
            if (!await repositorio.TablaExiste().ConfigureAwait(false))
            {
                return ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO>.Error(503, MENSAJE_SIN_TABLA);
            }
            string empresa = Empresa(peticion.Empresa);
            string anterior = peticion.ClienteAnterior?.Trim();
            string nuevo = peticion.ClienteNuevo?.Trim();
            DatosCambioFiscal datos = await repositorio.Leer(empresa, anterior, nuevo).ConfigureAwait(false);
            DecisionEnlaceCambioFiscal enlace = Enlace(datos, anterior, nuevo);
            if (!enlace.Vale)
            {
                return ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO>.Error(enlace.Estado, enlace.Motivo);
            }
            string sinPermiso = await motivoSinPermiso(datos.Anterior.Principal?.Vendedor).ConfigureAwait(false);
            if (sinPermiso != null)
            {
                return ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO>.Error(403, sinPermiso);
            }
            if (enlace.CrearEnlace && !ensayo)
            {
                await repositorio.CrearEnlace(empresa, nuevo, anterior, usuario).ConfigureAwait(false);
            }

            PasarPedidosCambioFiscalDTO respuesta = new PasarPedidosCambioFiscalDTO { Ensayo = ensayo, ClienteAnterior = anterior, ClienteNuevo = nuevo };
            foreach (int numero in peticion.Pedidos.Distinct())
            {
                respuesta.Pedidos.Add(await PasarUno(numero, anterior, nuevo, datos.Nuevo, ensayo, usuario, peticion.CreadoSinPasarValidacion, leerPedido, cambiarCliente).ConfigureAwait(false));
            }
            return ResultadoCambioFiscal<PasarPedidosCambioFiscalDTO>.Ok(respuesta);
        }

        private async Task<PedidoPasadoCambioFiscalDTO> PasarUno(int numero, string anterior, string nuevo, FotoClienteCambioFiscal fichaNueva, bool ensayo,
            string usuario, bool sinPasarValidacion, Func<int, Task<Tuple<string, string>>> leerPedido,
            Func<int, CambiarClientePedidoRequest, Task<ResultadoCambioClientePedido>> cambiarCliente)
        {
            PedidoPasadoCambioFiscalDTO item = new PedidoPasadoCambioFiscalDTO { Pedido = numero };
            Tuple<string, string> deQuien = await leerPedido(numero).ConfigureAwait(false);
            if (deQuien == null)
            {
                item.Codigo = "NO_EXISTE";
                item.Mensaje = $"No existe el pedido {numero}.";
                return item;
            }
            if (!ReglaCopiaCambioFiscal.Igual(deQuien.Item1, anterior))
            {
                item.Codigo = "NO_ES_DEL_CLIENTE_ANTERIOR";
                item.Mensaje = $"El pedido {numero} es del cliente {deQuien.Item1?.Trim()}, no del {anterior}.";
                return item;
            }
            // El mismo contacto si el nuevo lo tiene; si no, su principal
            string contacto = fichaNueva.Fichas.Any(f => ReglaCopiaCambioFiscal.Igual(f.Contacto, deQuien.Item2) && (f.ClientePrincipal || (f.Estado ?? 0) >= 0))
                ? deQuien.Item2?.Trim()
                : null;
            try
            {
                ResultadoCambioClientePedido r = await cambiarCliente(numero, new CambiarClientePedidoRequest
                {
                    Cliente = nuevo,
                    Contacto = contacto,
                    Ensayo = ensayo,
                    Usuario = usuario,
                    CreadoSinPasarValidacion = sinPasarValidacion
                }).ConfigureAwait(false);
                if (r.NoEncontrado)
                {
                    item.Codigo = "NO_EXISTE";
                    item.Mensaje = $"No existe el pedido {numero}.";
                }
                else if (r.Error != null)
                {
                    item.Codigo = r.Codigo ?? "NO_SE_PUEDE";
                    item.Mensaje = r.Error;
                }
                else
                {
                    item.Pasado = true;
                    item.TeniaPicking = r.Respuesta.TeniaPicking;
                    item.SinCambiosDeImportes = r.Respuesta.SinCambiosDeImportes;
                    item.Cambios = r.Respuesta.Cambios ?? new List<string>();
                    item.Avisos = r.Respuesta.Avisos ?? new List<string>();
                    item.Mensaje = ensayo
                        ? (r.Respuesta.SinCambiosDeImportes ? $"El pedido {numero} pasaría al {nuevo} sin cambiar ningún importe." : $"El pedido {numero} pasaría al {nuevo} con cambios.")
                        : $"El pedido {numero} es ahora del cliente {nuevo}.";
                }
            }
            catch (Exception ex)
            {
                item.Codigo = "ERROR";
                item.Mensaje = $"No se ha podido pasar el pedido {numero}: {ex.Message}";
            }
            return item;
        }

        private static DecisionEnlaceCambioFiscal Enlace(DatosCambioFiscal datos, string anterior, string nuevo)
        {
            return ReglaCopiaCambioFiscal.DecidirEnlace(anterior, nuevo, datos?.Anterior?.Existe == true, datos?.Nuevo?.Existe == true,
                datos?.AnterioresDelNuevo, datos?.OrigenNuevo);
        }

        private static CopiaCambioFiscalDTO Respuesta(string empresa, DatosCambioFiscal datos, PlanCopiaCambioFiscal plan, DecisionEnlaceCambioFiscal enlace, bool ensayo)
        {
            return new CopiaCambioFiscalDTO
            {
                Ensayo = ensayo,
                Empresa = empresa,
                ClienteAnterior = datos.Anterior.Cliente?.Trim(),
                NombreAnterior = datos.Anterior.Principal?.Nombre,
                ClienteNuevo = datos.Nuevo.Cliente?.Trim(),
                NombreNuevo = datos.Nuevo.Principal?.Nombre,
                Enlazados = enlace.Enlazados || (!ensayo && enlace.CrearEnlace),
                SeEnlazaAlCopiar = ensayo && enlace.CrearEnlace,
                MotivoNoSePuedeCopiar = enlace.Vale ? null : enlace.Motivo,
                Bloques = plan?.Bloques ?? new List<BloqueCambioFiscalDTO>()
            };
        }

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();
    }
}
