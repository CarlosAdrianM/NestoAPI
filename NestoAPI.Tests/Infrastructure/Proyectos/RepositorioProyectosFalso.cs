using NestoAPI.Infraestructure.Proyectos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NestoAPI.Tests.Infrastructure.Proyectos
{
    /// <summary>NestoAPI#901: repositorio en memoria que aplica las decisiones igual que el de SQL.</summary>
    internal class RepositorioProyectosFalso : IRepositorioProyectos
    {
        public bool HayTablas { get; set; } = true;
        public List<FilaProyecto> Proyectos { get; } = new List<FilaProyecto>();
        public List<FilaImputacion> Imputaciones { get; } = new List<FilaImputacion>();
        public List<FilaApunteContable> Apuntes { get; } = new List<FilaApunteContable>();
        public List<Tuple<string, string, string>> Rastro { get; } = new List<Tuple<string, string, string>>();
        public FiltroApuntesProyecto UltimoFiltroApuntes { get; private set; }
        public bool ApunteOcupado { get; set; }
        private int siguienteProyecto = 1;
        private int siguienteImputacion = 1;

        public FilaProyecto Proyecto(string codigo, string estado = ReglasProyectos.ESTADO_ABIERTO, string producto = null, string empresa = "1",
            DateTime? desde = null, DateTime? hasta = null)
        {
            var p = new FilaProyecto
            {
                Id = siguienteProyecto++,
                Empresa = empresa,
                Codigo = codigo,
                Nombre = "Proyecto " + codigo,
                Tipo = "CursoSEPE",
                FechaDesde = desde ?? new DateTime(2026, 1, 1),
                FechaHasta = hasta,
                Estado = estado,
                Producto = producto,
                Usuario = "Carlos",
                FechaAlta = new DateTime(2026, 1, 1)
            };
            Proyectos.Add(p);
            return p;
        }

        public FilaApunteContable Apunte(int orden, string cuenta, decimal debe, decimal haber, string empresa = "1", DateTime? fecha = null)
        {
            var a = new FilaApunteContable
            {
                Empresa = empresa,
                NumeroOrden = orden,
                Cuenta = cuenta,
                NombreCuenta = "Cuenta " + cuenta,
                Fecha = fecha ?? new DateTime(2026, 3, 10),
                Concepto = "Concepto " + orden,
                Documento = "F" + orden,
                Asiento = orden * 10,
                Debe = debe,
                Haber = haber,
                Tercero = "1234",
                TipoTercero = "Proveedor",
                NombreTercero = "LIBRERÍA"
            };
            Apuntes.Add(a);
            return a;
        }

        public FilaImputacion Imputar(FilaProyecto proyecto, FilaApunteContable apunte, decimal importe, string comentario = null)
        {
            FilaImputacion f = ReglasProyectos.Foto(apunte);
            f.Id = siguienteImputacion++;
            f.ProyectoId = proyecto.Id;
            f.Importe = importe;
            f.Comentario = comentario;
            f.Usuario = "Ana";
            f.Fecha = new DateTime(2026, 3, 11);
            f.CodigoProyecto = proyecto.Codigo;
            f.NombreProyecto = proyecto.Nombre;
            f.EstadoProyecto = proyecto.Estado;
            Imputaciones.Add(f);
            return f;
        }

        public Task<bool> TablasExisten() => Task.FromResult(HayTablas);

        public Task<List<FilaProyecto>> LeerProyectos(FiltroProyectos filtro) => Task.FromResult(Proyectos
            .Where(p => p.Empresa == filtro.Empresa)
            .Where(p => !filtro.SoloAbiertos || !p.EstaCerrado)
            .Where(p => filtro.Tipo == null || p.Tipo == filtro.Tipo)
            .Where(p => string.IsNullOrEmpty(filtro.Filtro) || p.Codigo.Contains(filtro.Filtro) || p.Nombre.Contains(filtro.Filtro))
            .Where(p => string.IsNullOrEmpty(filtro.Producto) || p.Producto == filtro.Producto)
            .Where(p => filtro.Ids == null || filtro.Ids.Contains(p.Id))
            .Select(p => p.Copia()).ToList());

        public Task<FilaProyecto> LeerProyecto(int id) => Task.FromResult(Proyectos.FirstOrDefault(p => p.Id == id)?.Copia());

        public Task<List<ResumenImputacionesProyecto>> LeerResumen(IList<int> ids) => Task.FromResult(Imputaciones
            .Where(i => ids.Contains(i.ProyectoId))
            .GroupBy(i => new { i.ProyectoId, i.Cuenta, Lado = i.Debe >= i.Haber ? 1 : -1 })
            .Select(g => new ResumenImputacionesProyecto { ProyectoId = g.Key.ProyectoId, Cuenta = g.Key.Cuenta, Lado = g.Key.Lado, Importe = g.Sum(i => i.Importe), Numero = g.Count() })
            .ToList());

        public Task<List<FilaImputacion>> LeerImputacionesDeProyectos(IList<int> ids) =>
            Task.FromResult(Imputaciones.Where(i => ids.Contains(i.ProyectoId)).ToList());

        public Task<ResultadoCrearProyecto> Crear(FilaProyecto proyecto, string usuario)
        {
            if (Proyectos.Any(p => p.Empresa == proyecto.Empresa && p.Codigo == proyecto.Codigo))
            {
                return Task.FromResult(new ResultadoCrearProyecto { CodigoRepetido = true });
            }
            FilaProyecto nuevo = proyecto.Copia();
            nuevo.Id = siguienteProyecto++;
            nuevo.Usuario = usuario;
            nuevo.FechaAlta = DateTime.Now;
            Proyectos.Add(nuevo);
            Rastro.Add(Tuple.Create(RepositorioProyectos.TABLA_RASTRO_PROYECTO, "(nuevo)", nuevo.Codigo));
            return Task.FromResult(new ResultadoCrearProyecto { Proyecto = nuevo.Copia() });
        }

        public Task<ResultadoCambioProyecto> Cambiar(int id, Func<FilaProyecto, int, DecisionCambioProyecto> decidir, string usuario)
        {
            FilaProyecto actual = Proyectos.FirstOrDefault(p => p.Id == id);
            DecisionCambioProyecto decision = decidir(actual?.Copia(), Imputaciones.Count(i => i.ProyectoId == id));
            if (decision.Error != null || decision.SinCambios)
            {
                if (decision.Nuevo == null && decision.Error == null)
                {
                    decision.Nuevo = actual?.Copia();
                }
                return Task.FromResult(new ResultadoCambioProyecto { Decision = decision });
            }
            if (decision.Borrar)
            {
                Proyectos.Remove(actual);
                Rastro.Add(Tuple.Create(RepositorioProyectos.TABLA_RASTRO_PROYECTO, actual.Codigo, "(borrado)"));
                return Task.FromResult(new ResultadoCambioProyecto { Decision = decision });
            }
            if (Proyectos.Any(p => p.Id != id && p.Empresa == decision.Nuevo.Empresa && p.Codigo == decision.Nuevo.Codigo))
            {
                return Task.FromResult(new ResultadoCambioProyecto { CodigoRepetido = true });
            }
            decision.Nuevo.UsuarioModificacion = usuario;
            Proyectos[Proyectos.IndexOf(actual)] = decision.Nuevo.Copia();
            Rastro.Add(Tuple.Create(RepositorioProyectos.TABLA_RASTRO_PROYECTO, actual.Estado + " " + actual.Codigo, decision.Nuevo.Estado + " " + decision.Nuevo.Codigo));
            return Task.FromResult(new ResultadoCambioProyecto { Decision = decision });
        }

        public Task<LecturaApuntesProyecto> LeerApuntesPorImputar(FiltroApuntesProyecto filtro)
        {
            UltimoFiltroApuntes = filtro;
            List<FilaApunteContable> apuntes = Apuntes.Where(a => a.Empresa == filtro.Empresa).OrderByDescending(a => a.Fecha).ThenByDescending(a => a.NumeroOrden).ToList();
            foreach (FilaApunteContable a in apuntes)
            {
                a.Imputado = Imputaciones.Where(i => i.NumeroOrdenApunte == a.NumeroOrden).Sum(i => i.Importe);
            }
            if (filtro.SoloPendientes)
            {
                apuntes = apuntes.Where(a => ReglasProyectos.ImporteApunte(a.Debe, a.Haber) - a.Imputado > 0.01M).ToList();
            }
            var lectura = new LecturaApuntesProyecto
            {
                Apuntes = apuntes.Skip(filtro.Pagina * filtro.Tope).Take(filtro.Tope).ToList(),
                HayMas = apuntes.Count > (filtro.Pagina + 1) * filtro.Tope
            };
            lectura.Imputaciones = Imputaciones.Where(i => lectura.Apuntes.Any(a => a.NumeroOrden == i.NumeroOrdenApunte)).ToList();
            return Task.FromResult(lectura);
        }

        public Task<Tuple<FilaApunteContable, List<FilaImputacion>>> LeerApunte(string empresa, int numeroOrden) => Task.FromResult(Tuple.Create(
            Apuntes.FirstOrDefault(a => a.Empresa == empresa && a.NumeroOrden == numeroOrden),
            Imputaciones.Where(i => i.Empresa == empresa && i.NumeroOrdenApunte == numeroOrden).ToList()));

        public Task<DecisionImputaciones> SustituirImputaciones(string empresa, int numeroOrden, IList<int> proyectosPedidos,
            Func<ContextoImputacionApunte, DecisionImputaciones> decidir, string usuario)
        {
            if (ApunteOcupado)
            {
                throw new ApunteOcupadoException(numeroOrden);
            }
            var contexto = new ContextoImputacionApunte
            {
                Apunte = Apuntes.FirstOrDefault(a => a.Empresa == empresa && a.NumeroOrden == numeroOrden),
                Actuales = Imputaciones.Where(i => i.Empresa == empresa && i.NumeroOrdenApunte == numeroOrden).ToList()
            };
            foreach (int id in contexto.Actuales.Select(a => a.ProyectoId).Concat(proyectosPedidos).Distinct())
            {
                FilaProyecto p = Proyectos.FirstOrDefault(x => x.Id == id);
                if (p != null)
                {
                    contexto.Proyectos[id] = p;
                }
            }
            DecisionImputaciones decision = decidir(contexto);
            if (decision.Error != null || decision.SinCambios)
            {
                return Task.FromResult(decision);
            }
            Imputaciones.RemoveAll(i => decision.IdsABorrar.Contains(i.Id));
            foreach (FilaImputacion f in decision.Insertar)
            {
                f.Id = siguienteImputacion++;
                Imputaciones.Add(f);
            }
            Rastro.Add(Tuple.Create(RepositorioProyectos.TABLA_RASTRO_IMPUTACION,
                RepositorioProyectos.TextoRastroReparto(empresa, numeroOrden, contexto.Actuales),
                RepositorioProyectos.TextoRastroReparto(empresa, numeroOrden, decision.Resultado)));
            return Task.FromResult(decision);
        }
    }
}
