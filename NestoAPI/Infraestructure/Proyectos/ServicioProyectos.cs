using NestoAPI.Models;
using NestoAPI.Models.Proyectos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Infraestructure.Proyectos
{
    /// <summary>NestoAPI#901: lo que devuelve el servicio: el valor o el error (que el controlador traduce a 400/403/404/409/503).</summary>
    public class ResultadoProyectos<T>
    {
        public T Valor { get; set; }
        public ErrorProyecto Error { get; set; }
        public bool Ok => Error == null;

        public static ResultadoProyectos<T> Bien(T valor) => new ResultadoProyectos<T> { Valor = valor };
        public static ResultadoProyectos<T> Mal(ErrorProyecto error) => new ResultadoProyectos<T> { Error = error };
    }

    /// <summary>NestoAPI#901: parámetros de GET api/Proyectos/ApuntesPorImputar tal como llegan.</summary>
    public class PeticionApuntesPorImputar
    {
        public string Empresa { get; set; }
        public DateTime? Desde { get; set; }
        public DateTime? Hasta { get; set; }
        public string Cuenta { get; set; }
        public string Proveedor { get; set; }
        public string Documento { get; set; }
        public string Texto { get; set; }
        public bool SoloPendientes { get; set; } = true;
        public int Pagina { get; set; }
        public int? Tope { get; set; }
    }

    /// <summary>NestoAPI#901: proyectos, reparto de apuntes y cuenta de explotación.</summary>
    public class ServicioProyectos
    {
        public const string MENSAJE_SIN_TABLA = "Todavía no existen las tablas de proyectos: hay que lanzar Scripts/Issue901_Proyectos.sql.";
        public const int TOPE_APUNTES = 500;
        public const int DIAS_MAXIMOS_APUNTES = 731;
        public const int MAXIMO_COMPARATIVA = 10;

        private readonly IRepositorioProyectos repositorio;
        private readonly Func<DateTime> ahora;

        public ServicioProyectos(IRepositorioProyectos repositorio, Func<DateTime> ahora = null)
        {
            this.repositorio = repositorio ?? throw new ArgumentNullException(nameof(repositorio));
            this.ahora = ahora ?? (() => DateTime.Now);
        }

        private static ErrorProyecto SinTabla() => new ErrorProyecto { Tipo = TipoErrorProyecto.SinTabla, Codigo = "SIN_TABLA", Mensaje = MENSAJE_SIN_TABLA };

        private static string Empresa(string empresa) => string.IsNullOrWhiteSpace(empresa) ? Constantes.Empresas.EMPRESA_POR_DEFECTO : empresa.Trim();

        // ------------------------------------------------------------------ Proyectos

        public async Task<ResultadoProyectos<List<ProyectoDTO>>> Listar(string empresa, bool soloAbiertos, string tipo, string filtro)
        {
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<List<ProyectoDTO>>.Mal(SinTabla());
            }
            string elTipo = null;
            if (!string.IsNullOrWhiteSpace(tipo))
            {
                elTipo = ReglasProyectos.NormalizarTipo(tipo);
                if (elTipo == null)
                {
                    return ResultadoProyectos<List<ProyectoDTO>>.Mal(ErrorProyecto.Invalido(
                        $"«{tipo.Trim()}» no es un tipo de proyecto: tiene que ser {string.Join(", ", ReglasProyectos.TIPOS)}."));
                }
            }
            List<FilaProyecto> filas = await repositorio.LeerProyectos(new FiltroProyectos
            {
                Empresa = Empresa(empresa),
                SoloAbiertos = soloAbiertos,
                Tipo = elTipo,
                Filtro = filtro
            }).ConfigureAwait(false);
            return ResultadoProyectos<List<ProyectoDTO>>.Bien(await ConTotales(filas).ConfigureAwait(false));
        }

        private async Task<List<ProyectoDTO>> ConTotales(List<FilaProyecto> filas)
        {
            List<ResumenImputacionesProyecto> resumen = filas.Count == 0
                ? new List<ResumenImputacionesProyecto>()
                : await repositorio.LeerResumen(filas.Select(f => f.Id).ToList()).ConfigureAwait(false);
            ILookup<int, ResumenImputacionesProyecto> porProyecto = resumen.ToLookup(r => r.ProyectoId);
            return filas.Select(f =>
            {
                ProyectoDTO dto = CalculoCuentaExplotacionProyecto.Dto(f);
                CalculoCuentaExplotacionProyecto.PonerTotales(dto, porProyecto[f.Id]);
                return dto;
            }).ToList();
        }

        public async Task<ResultadoProyectos<ProyectoDTO>> Leer(int id)
        {
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(SinTabla());
            }
            FilaProyecto fila = await repositorio.LeerProyecto(id).ConfigureAwait(false);
            if (fila == null)
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(ErrorProyecto.NoExiste($"No existe el proyecto {id}."));
            }
            return ResultadoProyectos<ProyectoDTO>.Bien((await ConTotales(new List<FilaProyecto> { fila }).ConfigureAwait(false)).Single());
        }

        public async Task<ResultadoProyectos<ProyectoDTO>> Crear(DatosProyecto datos, string usuario)
        {
            List<string> faltas = ReglasProyectos.Validar(datos, out FilaProyecto limpio);
            if (faltas.Any())
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(ErrorProyecto.Invalido(string.Join(" ", faltas)));
            }
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(SinTabla());
            }
            ResultadoCrearProyecto resultado = await repositorio.Crear(limpio, usuario).ConfigureAwait(false);
            if (resultado.CodigoRepetido)
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(CodigoRepetido(limpio.Codigo, limpio.Empresa));
            }
            ProyectoDTO dto = CalculoCuentaExplotacionProyecto.Dto(resultado.Proyecto);
            CalculoCuentaExplotacionProyecto.PonerTotales(dto, null);
            return ResultadoProyectos<ProyectoDTO>.Bien(dto);
        }

        private static ErrorProyecto CodigoRepetido(string codigo, string empresa) => ErrorProyecto.Conflicto("CODIGO_REPETIDO",
            $"Ya hay un proyecto {codigo} en la empresa {empresa}: elija otro código (por ejemplo, con el número de convocatoria al final).");

        public async Task<ResultadoProyectos<ProyectoDTO>> Modificar(int id, DatosProyecto datos, string usuario)
        {
            List<string> faltas = ReglasProyectos.Validar(datos, out FilaProyecto limpio);
            if (faltas.Any())
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(ErrorProyecto.Invalido(string.Join(" ", faltas)));
            }
            return await Cambiar(id, (actual, n) => ReglasProyectos.DecidirModificacion(actual, limpio), usuario, limpio.Codigo).ConfigureAwait(false);
        }

        public Task<ResultadoProyectos<ProyectoDTO>> Cerrar(int id, string usuario) =>
            Cambiar(id, (actual, n) => ReglasProyectos.DecidirCierre(actual), usuario, null);

        public Task<ResultadoProyectos<ProyectoDTO>> Reabrir(int id, string usuario) =>
            Cambiar(id, (actual, n) => ReglasProyectos.DecidirReapertura(actual), usuario, null);

        public async Task<ResultadoProyectos<bool>> Borrar(int id, string usuario)
        {
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<bool>.Mal(SinTabla());
            }
            ResultadoCambioProyecto resultado = await repositorio.Cambiar(id, ReglasProyectos.DecidirBorrado, usuario).ConfigureAwait(false);
            if (resultado.Decision?.Error != null)
            {
                return ResultadoProyectos<bool>.Mal(resultado.Decision.Error);
            }
            return ResultadoProyectos<bool>.Bien(true);
        }

        private async Task<ResultadoProyectos<ProyectoDTO>> Cambiar(int id, Func<FilaProyecto, int, DecisionCambioProyecto> decidir, string usuario, string codigo)
        {
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ProyectoDTO>.Mal(SinTabla());
            }
            ResultadoCambioProyecto resultado = await repositorio.Cambiar(id, decidir, usuario).ConfigureAwait(false);
            if (resultado.CodigoRepetido)
            {
                FilaProyecto actual = await repositorio.LeerProyecto(id).ConfigureAwait(false);
                return ResultadoProyectos<ProyectoDTO>.Mal(CodigoRepetido(codigo, actual?.Empresa ?? Constantes.Empresas.EMPRESA_POR_DEFECTO));
            }
            if (resultado.Decision?.Error != null)
            {
                ErrorProyecto error = resultado.Decision.Error;
                if (error.Tipo == TipoErrorProyecto.NoExiste)
                {
                    error.Mensaje = $"No existe el proyecto {id}.";
                }
                return ResultadoProyectos<ProyectoDTO>.Mal(error);
            }
            FilaProyecto nuevo = resultado.Decision?.Nuevo;
            return ResultadoProyectos<ProyectoDTO>.Bien((await ConTotales(new List<FilaProyecto> { nuevo }).ConfigureAwait(false)).Single());
        }

        // ------------------------------------------------------------------ Apuntes

        /// <summary>Valida la petición de apuntes por imputar y la deja lista para el repositorio. Pura.</summary>
        internal static ErrorProyecto ValidarApuntes(PeticionApuntesPorImputar p, DateTime hoy, out FiltroApuntesProyecto filtro)
        {
            filtro = new FiltroApuntesProyecto
            {
                Empresa = Empresa(p.Empresa),
                Desde = (p.Desde ?? new DateTime(hoy.Year, 1, 1)).Date,
                Hasta = (p.Hasta ?? hoy).Date,
                Tercero = string.IsNullOrWhiteSpace(p.Proveedor) ? null : p.Proveedor.Trim(),
                Documento = string.IsNullOrWhiteSpace(p.Documento) ? null : p.Documento.Trim(),
                Texto = string.IsNullOrWhiteSpace(p.Texto) ? null : p.Texto.Trim(),
                SoloPendientes = p.SoloPendientes,
                Pagina = Math.Max(0, p.Pagina),
                Tope = Math.Min(TOPE_APUNTES, Math.Max(1, p.Tope ?? TOPE_APUNTES))
            };
            if (filtro.Hasta < filtro.Desde)
            {
                return ErrorProyecto.Invalido("La fecha «hasta» no puede ser anterior a «desde».");
            }
            if ((filtro.Hasta - filtro.Desde).TotalDays > DIAS_MAXIMOS_APUNTES)
            {
                return ErrorProyecto.Invalido($"Busque como mucho {DIAS_MAXIMOS_APUNTES} días de apuntes cada vez.");
            }
            if (filtro.Documento?.Length > 10 || filtro.Tercero?.Length > 10 || filtro.Texto?.Length > 50)
            {
                return ErrorProyecto.Invalido("El documento y el proveedor tienen como mucho 10 caracteres y el texto 50.");
            }
            if (!string.IsNullOrWhiteSpace(p.Cuenta))
            {
                foreach (string cuenta in p.Cuenta.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).Distinct())
                {
                    if (cuenta.Length > 10 || !cuenta.All(char.IsDigit))
                    {
                        return ErrorProyecto.Invalido($"«{cuenta}» no es una cuenta: escriba las primeras cifras (por ejemplo, 623 o 705).");
                    }
                    if (cuenta[0] != '6' && cuenta[0] != '7')
                    {
                        return ErrorProyecto.Invalido($"La cuenta {cuenta} no es de gastos (6) ni de ingresos (7): a los proyectos solo se imputan esas.");
                    }
                    filtro.Cuentas.Add(cuenta);
                }
                if (filtro.Cuentas.Count > 10)
                {
                    return ErrorProyecto.Invalido("Busque como mucho 10 cuentas a la vez.");
                }
            }
            return null;
        }

        public async Task<ResultadoProyectos<ApuntesPorImputarDTO>> ApuntesPorImputar(PeticionApuntesPorImputar peticion)
        {
            ErrorProyecto error = ValidarApuntes(peticion ?? new PeticionApuntesPorImputar(), ahora(), out FiltroApuntesProyecto filtro);
            if (error != null)
            {
                return ResultadoProyectos<ApuntesPorImputarDTO>.Mal(error);
            }
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ApuntesPorImputarDTO>.Mal(SinTabla());
            }
            LecturaApuntesProyecto lectura = await repositorio.LeerApuntesPorImputar(filtro).ConfigureAwait(false);
            ILookup<int, FilaImputacion> porApunte = lectura.Imputaciones.ToLookup(i => i.NumeroOrdenApunte);
            return ResultadoProyectos<ApuntesPorImputarDTO>.Bien(new ApuntesPorImputarDTO
            {
                Empresa = filtro.Empresa,
                Desde = filtro.Desde,
                Hasta = filtro.Hasta,
                Pagina = filtro.Pagina,
                Tope = filtro.Tope,
                HayMas = lectura.HayMas,
                Apuntes = lectura.Apuntes.Select(a => DtoApunte(a, porApunte[a.NumeroOrden].ToList())).ToList()
            });
        }

        internal static ApunteProyectoDTO DtoApunte(FilaApunteContable a, List<FilaImputacion> imputaciones)
        {
            decimal importe = ReglasProyectos.ImporteApunte(a.Debe, a.Haber);
            decimal imputado = imputaciones.Sum(i => i.Importe);
            NaturalezaApunte naturaleza = ReglasProyectos.Naturaleza(a.Cuenta);
            return new ApunteProyectoDTO
            {
                Empresa = a.Empresa?.Trim(),
                NumeroOrden = a.NumeroOrden,
                Fecha = a.Fecha,
                Cuenta = a.Cuenta?.Trim(),
                NombreCuenta = a.NombreCuenta?.Trim(),
                Concepto = a.Concepto?.Trim(),
                Documento = a.Documento?.Trim(),
                Asiento = a.Asiento,
                Diario = a.Diario?.Trim(),
                Debe = a.Debe,
                Haber = a.Haber,
                ImporteApunte = importe,
                Naturaleza = naturaleza == NaturalezaApunte.NoImputable ? null : naturaleza.ToString(),
                EsAbono = ReglasProyectos.EsAbono(a.Cuenta, a.Debe, a.Haber),
                Tercero = a.Tercero?.Trim(),
                TipoTercero = a.TipoTercero,
                NombreTercero = a.NombreTercero?.Trim(),
                Imputado = imputado,
                Pendiente = importe - imputado,
                PorcentajeImputado = ReglasProyectos.Porcentaje(imputado, importe),
                Imputaciones = imputaciones.Select(i => DtoImputacion(i, importe)).ToList()
            };
        }

        internal static ImputacionApunteDTO DtoImputacion(FilaImputacion i, decimal importeApunte) => new ImputacionApunteDTO
        {
            Id = i.Id,
            ProyectoId = i.ProyectoId,
            CodigoProyecto = i.CodigoProyecto?.Trim(),
            NombreProyecto = i.NombreProyecto?.Trim(),
            EstadoProyecto = i.EstadoProyecto?.Trim(),
            Importe = i.Importe,
            Porcentaje = ReglasProyectos.Porcentaje(i.Importe, importeApunte),
            Comentario = i.Comentario,
            Usuario = i.Usuario?.Trim(),
            Fecha = i.Fecha == default ? (DateTime?)null : i.Fecha
        };

        public async Task<ResultadoProyectos<ImputacionesApunteDTO>> ImputacionesDeApunte(string empresa, int numeroOrden)
        {
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ImputacionesApunteDTO>.Mal(SinTabla());
            }
            string laEmpresa = Empresa(empresa);
            Tuple<FilaApunteContable, List<FilaImputacion>> lectura = await repositorio.LeerApunte(laEmpresa, numeroOrden).ConfigureAwait(false);
            if (lectura.Item1 == null && lectura.Item2.Count == 0)
            {
                return ResultadoProyectos<ImputacionesApunteDTO>.Mal(ErrorProyecto.NoExiste($"No existe el apunte {numeroOrden} en la empresa {laEmpresa}."));
            }
            return ResultadoProyectos<ImputacionesApunteDTO>.Bien(DtoImputaciones(laEmpresa, numeroOrden, lectura.Item1, lectura.Item2));
        }

        private static ImputacionesApunteDTO DtoImputaciones(string empresa, int numeroOrden, FilaApunteContable apunte, List<FilaImputacion> imputaciones)
        {
            decimal importe = apunte == null ? imputaciones.Select(i => ReglasProyectos.ImporteApunte(i.Debe, i.Haber)).DefaultIfEmpty(0).First()
                : ReglasProyectos.ImporteApunte(apunte.Debe, apunte.Haber);
            return new ImputacionesApunteDTO
            {
                Empresa = empresa,
                NumeroOrden = numeroOrden,
                Apunte = apunte == null ? null : DtoApunte(apunte, imputaciones),
                Imputaciones = imputaciones.Select(i => DtoImputacion(i, importe)).ToList()
            };
        }

        public async Task<ResultadoProyectos<ImputacionesApunteDTO>> RepartirApunte(int numeroOrden, PeticionImputacionesApunteDTO peticion, string usuario)
        {
            if (peticion == null)
            {
                return ResultadoProyectos<ImputacionesApunteDTO>.Mal(ErrorProyecto.Invalido("Falta el reparto del apunte (una lista vacía quita lo imputado)."));
            }
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ImputacionesApunteDTO>.Mal(SinTabla());
            }
            string empresa = Empresa(peticion.Empresa);
            List<ImputacionPedida> pedidas = (peticion.Imputaciones ?? new List<ImputacionPedidaDTO>()).Where(i => i != null)
                .Select(i => new ImputacionPedida { ProyectoId = i.ProyectoId, Importe = i.Importe, Comentario = i.Comentario }).ToList();
            DateTime momento = ahora();
            DecisionImputaciones decision;
            try
            {
                decision = await repositorio.SustituirImputaciones(empresa, numeroOrden, pedidas.Select(p => p.ProyectoId).Distinct().ToList(),
                    contexto => ReglasProyectos.DecidirReparto(contexto, numeroOrden, peticion.ImporteApunte, pedidas, usuario, momento), usuario).ConfigureAwait(false);
            }
            catch (ApunteOcupadoException ex)
            {
                return ResultadoProyectos<ImputacionesApunteDTO>.Mal(ErrorProyecto.Conflicto("APUNTE_OCUPADO", ex.Message));
            }
            if (decision.Error != null)
            {
                return ResultadoProyectos<ImputacionesApunteDTO>.Mal(decision.Error);
            }
            Tuple<FilaApunteContable, List<FilaImputacion>> lectura = await repositorio.LeerApunte(empresa, numeroOrden).ConfigureAwait(false);
            ImputacionesApunteDTO dto = DtoImputaciones(empresa, numeroOrden, lectura.Item1, lectura.Item2);
            dto.Cambiado = !decision.SinCambios;
            dto.Avisos = decision.Avisos;
            return ResultadoProyectos<ImputacionesApunteDTO>.Bien(dto);
        }

        // ------------------------------------------------------------------ Informes

        public async Task<ResultadoProyectos<CuentaExplotacionProyectoDTO>> CuentaExplotacion(int id)
        {
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<CuentaExplotacionProyectoDTO>.Mal(SinTabla());
            }
            FilaProyecto proyecto = await repositorio.LeerProyecto(id).ConfigureAwait(false);
            if (proyecto == null)
            {
                return ResultadoProyectos<CuentaExplotacionProyectoDTO>.Mal(ErrorProyecto.NoExiste($"No existe el proyecto {id}."));
            }
            List<FilaImputacion> imputaciones = await repositorio.LeerImputacionesDeProyectos(new List<int> { id }).ConfigureAwait(false);
            return ResultadoProyectos<CuentaExplotacionProyectoDTO>.Bien(CalculoCuentaExplotacionProyecto.Calcular(proyecto, imputaciones));
        }

        /// <summary>Lee los ids «1,2,3». Puro.</summary>
        internal static ErrorProyecto LeerIds(string ids, out List<int> lista)
        {
            lista = new List<int>();
            foreach (string trozo in (ids ?? string.Empty).Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!int.TryParse(trozo.Trim(), out int id) || id <= 0)
                {
                    return ErrorProyecto.Invalido($"«{trozo.Trim()}» no es un número de proyecto.");
                }
                if (!lista.Contains(id))
                {
                    lista.Add(id);
                }
            }
            return null;
        }

        public async Task<ResultadoProyectos<ComparativaProyectosDTO>> Comparativa(string ids, string producto, string empresa)
        {
            ErrorProyecto error = LeerIds(ids, out List<int> lista);
            if (error != null)
            {
                return ResultadoProyectos<ComparativaProyectosDTO>.Mal(error);
            }
            if (lista.Count == 0 && string.IsNullOrWhiteSpace(producto))
            {
                return ResultadoProyectos<ComparativaProyectosDTO>.Mal(ErrorProyecto.Invalido("Diga qué proyectos comparar (ids=1,2,3) o de qué curso (producto=IMPE0210)."));
            }
            if (lista.Count > MAXIMO_COMPARATIVA)
            {
                return ResultadoProyectos<ComparativaProyectosDTO>.Mal(ErrorProyecto.Invalido($"Compare como mucho {MAXIMO_COMPARATIVA} proyectos a la vez."));
            }
            if (!await repositorio.TablasExisten().ConfigureAwait(false))
            {
                return ResultadoProyectos<ComparativaProyectosDTO>.Mal(SinTabla());
            }
            List<FilaProyecto> proyectos = await repositorio.LeerProyectos(new FiltroProyectos
            {
                Empresa = Empresa(empresa),
                Ids = lista.Count > 0 ? lista : null,
                Producto = lista.Count > 0 ? null : producto.Trim()
            }).ConfigureAwait(false);
            if (lista.Count > 0)
            {
                List<int> faltan = lista.Where(i => proyectos.All(p => p.Id != i)).ToList();
                if (faltan.Any())
                {
                    return ResultadoProyectos<ComparativaProyectosDTO>.Mal(ErrorProyecto.NoExiste(
                        $"No {(faltan.Count == 1 ? "existe el proyecto" : "existen los proyectos")} {string.Join(", ", faltan)} en la empresa {Empresa(empresa)}."));
                }
                proyectos = lista.Select(i => proyectos.First(p => p.Id == i)).ToList();
            }
            else
            {
                if (proyectos.Count == 0)
                {
                    return ResultadoProyectos<ComparativaProyectosDTO>.Mal(ErrorProyecto.NoExiste($"No hay proyectos del producto {producto.Trim().ToUpperInvariant()}."));
                }
                proyectos = proyectos.OrderBy(p => p.FechaDesde).ThenBy(p => p.Codigo).Take(MAXIMO_COMPARATIVA).ToList();
            }
            List<FilaImputacion> imputaciones = await repositorio.LeerImputacionesDeProyectos(proyectos.Select(p => p.Id).ToList()).ConfigureAwait(false);
            return ResultadoProyectos<ComparativaProyectosDTO>.Bien(CalculoCuentaExplotacionProyecto.Comparar(proyectos, imputaciones));
        }
    }
}
