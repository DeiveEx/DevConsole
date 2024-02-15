# DevConsole
 An in-game console that can be used to see Unity logs and/or execute custom commands
 
 You can register methods (with parameters) from classes to be used as console commands, as well as parameterless lambdas. The system will use reflection to hold a reference of the method and try to call the correct one accordingly to the parameters of all overloads of the defined method.
 
 The system also accepts recursive methods, so you can call one method inside another to use the result as a parameter.
