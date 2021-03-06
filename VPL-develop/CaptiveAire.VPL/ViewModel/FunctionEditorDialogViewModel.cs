﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using CaptiveAire.VPL.Extensions;
using CaptiveAire.VPL.Interfaces;
using CaptiveAire.VPL.Metadata;
using CaptiveAire.VPL.Model;
using CaptiveAire.VPL.View;
using Cas.Common.WPF;
using Cas.Common.WPF.Behaviors;
using Cas.Common.WPF.Interfaces;
using GalaSoft.MvvmLight;
using GalaSoft.MvvmLight.CommandWpf;

namespace CaptiveAire.VPL.ViewModel
{
    internal class FunctionEditorDialogViewModel : ViewModelBase, IFunctionEditorDialogViewModel
    {
        private readonly IVplServiceContext _context;
        private readonly Function _function;
        private readonly Action<FunctionMetadata> _saveAction;
        private readonly ITextEditService _textEditService;
        private readonly string _displayName;
        private readonly IFunctionEditorManager _functionEditorManager;
        private CancellationTokenSource _cts;
        private readonly ToolsViewModel<IElementFactory> _tools;
        private IVplType _selectedType;
        private readonly IMessageBoxService _messageBoxService = new MessageBoxService();
        private ErrorViewModel[] _errors;
        private double _scale = 1;
        private bool _isErrorsExpanded;
        private ICallStack _callStack;
        private bool _isCallStackExpanded;

        public FunctionEditorDialogViewModel(IVplServiceContext context, Function function, Action<FunctionMetadata> saveAction, ITextEditService textEditService, string displayName, IFunctionEditorManager functionEditorManager)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (saveAction == null) throw new ArgumentNullException(nameof(saveAction));
            if (textEditService == null) throw new ArgumentNullException(nameof(textEditService));
            if (functionEditorManager == null) throw new ArgumentNullException(nameof(functionEditorManager));

            _context = context;
            _function = function;
            _saveAction = saveAction;
            _textEditService = textEditService;
            _displayName = displayName;
            _functionEditorManager = functionEditorManager;

            //Commands
            RunCommand = new RelayCommand(Run, CanRun);
            StopCommand = new RelayCommand(Stop, CanStop);
            OkCommand = new RelayCommand(Ok, CanOk);
            CloseCommand = new RelayCommand(Cancel, CanCancel);
            SaveCommand = new RelayCommand(Apply, CanOk);
            AddVariableCommand = new RelayCommand(AddVariable, CanAddVariable);
            PasteCommand = new RelayCommand(Paste, CanPaste);
            SelectReturnTypeCommand = new RelayCommand(SelectReturnType);
            ClearReturnTypeCommand = new RelayCommand(() => ClearReturnType(), CanClearReturnType);
            CheckForErrorsCommand = new RelayCommand(CheckForErrors);
            ResetZoomCommand = new RelayCommand(ResetZoom);

            //Create the toolbox
            _tools = new ToolsViewModel<IElementFactory>(context.ElementFactoryManager.Factories.Where(f => f.ShowInToolbox));

            //Select a default type
            SelectedType = context.Types.FirstOrDefault(t => t.Id == VplTypeId.Float);

            function.PropertyChanged += Function_PropertyChanged;

            //Save the initial state
            function.SaveUndoState();

            //Register this window.
            _functionEditorManager.Register(this);

            //Number the statements
            function.NumberStatements();
        }

        private void Function_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Function.Name))
            {
                RaisePropertyChanged(nameof(Title));
            }
        }

        public ICommand RunCommand { get; }
        public ICommand StopCommand { get; }
        public ICommand OkCommand { get; }
        public ICommand CloseCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand AddVariableCommand { get; }
        public ICommand PasteCommand { get; }
        public ICommand SelectReturnTypeCommand { get; }
        public ICommand ClearReturnTypeCommand { get; }
        public ICommand CheckForErrorsCommand { get; }
        public ICommand ResetZoomCommand { get; }
      
        private void CheckForErrors()
        {
            Errors = null;

            var errors = Function.CheckForErrors();

            if (errors.Length == 0)
            {
                _messageBoxService.Show("No errors or warnings found.");
            }
            else
            {
                Errors = errors
                    .Select(e => new ErrorViewModel(e))
                    .ToArray();

                IsErrorsExpanded = true;
            }
        }

        protected void ResetZoom()
        {
            Scale = 1.0;
        }

        public double ScaleMin
        {
            get { return 0.1; }
        }

        public double ScaleMax
        {
            get { return 2.0; }
        }

        public double Scale
        {
            get { return _scale; }
            set
            {
                _scale = value; 
                RaisePropertyChanged();
            }
        }

        private void SelectReturnType()
        {
            //Create the view model
            var viewModel = new SelectTypeDialogViewModel(_context.Types)
            {
                SelectedTypeId = Function.ReturnTypeId
            };

            var view = new SelectTypeDialogView()
            {
                Owner = WindowUtil.GetActiveWindow(),
                DataContext = viewModel
            };

            if (view.ShowDialog() == true)
            {
                if (ClearReturnType())
                {

                    Function.ReturnTypeId = viewModel.SelectedTypeId;

                    if (Function.ReturnTypeId != null)
                    {
                        //Get the type
                        var type = Function.GetVplTypeOrThrow(Function.ReturnTypeId.Value);

                        //Add the return variable
                        Function.AddVariable(new ReturnValueVariable(Function, type));
                    }
                }
            }
        }

        private bool ClearReturnType()
        {
            //Check to see if the return variable is in use.
            var variable = Function.GetVariable(ReturnValueVariable.ReturnVariableId);

            if (variable == null)
                return true;
            
            //Try to delete the variable
            if (variable.Delete())
            {
                Function.ReturnTypeId = null;

                return true;
            }

            return false;
        }

        public ErrorViewModel[] Errors
        {
            get { return _errors; }
            private set
            {
                _errors = value; 
                RaisePropertyChanged();
            }
        }

        private bool CanClearReturnType()
        {
            return _function.ReturnTypeId.HasValue;
        }

        private void Paste()
        {
            var data = ClipboardUtility.Paste();

            if (data != null)
            {
                Function.Drop(data, true);

                Function.SaveUndoState();
            }
        }

        private bool CanPaste()
        {
            return ClipboardUtility.CanPaste();
        }

        private void AddVariable()
        {
            //Create a name for the variable
            var name = Function.Variables
                .Select(v => v.Name)
                .CreateUniqueName($"{SelectedType.Name} {{0}}");

            bool wasAccepted = false;

            //Edit the name
            _textEditService.EditText(name, "Name", "Add Variable", t =>
            {
                name = t;
                wasAccepted = true;
            }, t => !string.IsNullOrWhiteSpace(t));

            //See if the user pressed 'ok'
            if (wasAccepted)
            {
                //Create the variable
                var variable = new Variable(Function, SelectedType, Guid.NewGuid())
                {
                    Name = name
                };

                //Add it to the function
                Function.AddVariable(variable);
            }
        }

        private bool CanAddVariable()
        {
            return SelectedType != null;
        }

        public IVplType SelectedType
        {
            get { return _selectedType; }
            set
            {
                _selectedType = value; 
                RaisePropertyChanged();
            }
        }

        public ToolsViewModel<IElementFactory> Tools
        {
            get { return _tools; }
        }

        private string DisplayName
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_displayName))
                    return _displayName;

                return Function.Name;
            }
        }

        public string Title
        {
            get { return $"Function - {DisplayName}"; }
        }

        private void Cancel()
        {
            Close?.Invoke(this, new CloseEventArgs(false));
        }

        private bool CanCancel()
        {
            return true;
        }

        private void Ok()
        {
            Apply();

            Close?.Invoke(this, new CloseEventArgs(true));
        }

        private bool CanOk()
        {
            return Function.IsDirty;
        }

        private bool Save()
        {
            try
            {
                _saveAction(GetMetadata());

                Function.MarkClean();

                return true;
            }
            catch (Exception ex)
            {
                _messageBoxService.Show(ex.Message, "Unable to save");
            }

            return false;
        }

        private void Apply()
        {
            Save();
        }

        private bool SaveIfDirty()
        {
            if (!Function.IsDirty)
                return true;

            var result = _messageBoxService.Show("Save changes?", "Function has changed", MessageBoxButton.YesNoCancel);

            switch (result)
            {
                case MessageBoxResult.Yes:
                    return Save();

                case MessageBoxResult.No:
                    return true;

               case MessageBoxResult.Cancel:
                    return false;

                default:
                    _messageBoxService.Show("That was odd.");
                    return false;
            }
        }

        private void Run()
        {
            RunInner(Function);
        }

        private bool CanRun()
        {
            return _cts == null;
        }

        private async void RunInner(IFunction function)
        {
            try
            {
                Errors = null;

                _cts = new CancellationTokenSource();

                //Create the execution environment
                using (var context = _context.VplService.CreateExecutionContext())
                {
                    //Let's expose the callstack so the UI can see it.
                    CallStack = context.CallStack;

                    try
                    {
                        //Excecute it!
                        await context.ExecuteAsync(function, new object[] { }, _cts.Token);
                    }
                    catch (Exception ex)
                    {
                        _messageBoxService.Show($"{ex.Message}\n\n{ex.StackTrace}", $"Error in '{context.CallStack.CurrentFrame?.Name}'", MessageBoxButton.OK, MessageBoxImage.Exclamation);

                        return;
                    }
                }
                
                //Let the user know that we're done.
                _messageBoxService.Show("Done");
            }
            finally 
            {
                _cts = null;

                CallStack = null;
            }
        }

        public ICallStack CallStack
        {
            get { return _callStack; }
            private set
            {
                _callStack = value;
                RaisePropertyChanged();
            }
        }

        private void Stop()
        {
            _cts?.Cancel();
        }

        private bool CanStop()
        {
            return _cts != null;
        }

        public Function Function
        {
            get { return _function; }
        }

        public FunctionMetadata GetMetadata()
        {
            return _function.ToMetadata();
        }

        public bool IsErrorsExpanded
        {
            get { return _isErrorsExpanded; }
            set
            {
                _isErrorsExpanded = value; 
                RaisePropertyChanged();
            }
        }

        public bool IsCallStackExpanded
        {
            get { return _isCallStackExpanded; }
            set
            {
                _isCallStackExpanded = value; 
                RaisePropertyChanged();
            }
        }

        #region ICloseable

        public bool CanClose()
        {
            if (_cts != null)
            {
                _messageBoxService.Show("Cannot close while running");
                return false;
            }

            return SaveIfDirty();
        }

        public void Closed()
        {
            _functionEditorManager.Unregister(this);
        }

        #endregion

        public event EventHandler<CloseEventArgs> Close;

        #region IActivateable

        public event EventHandler Activated;

        public void Activate()
        {
            Activated?.Invoke(this, EventArgs.Empty);
        }

        #endregion
    }
}