using UnityEngine;
using Ignix.Debug.Console;

public class DevConsoleTester : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
	    DevConsole devConsole = DevConsole.Instance;

		devConsole.RegisterCommand("add", "Add", "Add", this, "Math");
		devConsole.RegisterCommand("sub", "Sub", "Sub", this, "Math");
		devConsole.RegisterCommand("mul", "Mul", "Mul", this, "Math");
		devConsole.RegisterCommand("div", "Div", "Div", this, "Math");

		devConsole.RegisterCommand("test", "this is a test", "Test", this);
		devConsole.RegisterCommand("testReturn", "this is a test", "GetTestFloat", this);
		devConsole.RegisterCommand("getObject", "Get an object from the scene by name", "GetObject", this);
		devConsole.RegisterCommand("setPos", "Set the position of an object", "SetPos", this);
	}

	#region Tests
	
	private void Test()
	{
		Debug.Log("Test command");
	}

	private void Test(int a)
	{
		Debug.Log($"Test command overload(int): {a}");
	}

	private void Test(float a)
	{
		Debug.Log($"Test command overload(float): {a}");
	}

	private void Test(string a)
	{
		Debug.Log($"Test command overload(string): {a}");
	}

	private void Test(Vector2 a)
	{
		Debug.Log($"Test command overload(Vector2): {a}");
	}

	private void Test(int a, int b)
	{
		Debug.Log($"Test command overload(int, int): {a}; {b}");
	}

	private void Test(int a, string b)
	{
		Debug.Log($"Test command overload(int, string): {a}; {b}");
	}

	private void Test(int a, Vector2 b)
	{
		Debug.Log($"Test command overload(int, Vector2): {a}; {b}");
	}

	private void Test(Vector2 a, Vector2 b)
	{
		Debug.Log($"Test command overload(Vector2, Vector2): {a}; {b}");
	}

	private float GetTestFloat()
	{
		Debug.Log($"returning float '2'");
		return 2f;
	}

	private float Add(float a, float b)
	{
		return a + b;
	}

	private float Sub(float a, float b)
	{
		return a - b;
	}

	private float Mul(float a, float b)
	{
		return a * b;
	}

	private float Div(float a, float b)
	{
		return a / b;
	}

	private void Print(float a, int b)
	{
		for (int i = 0; i < b; i++)
		{
			Debug.Log(a.ToString());
		}
	}

	public Transform GetObject(string name)
	{
		return GameObject.Find(name).transform;
	}

	public void SetPos(Transform t, Vector3 pos)
	{
		t.position = pos;
	}
	
	[DevCommand]
	private void TestAttribute()
	{
		Debug.Log("This is a method that was registered using an Attribute");
	}
	
	[DevCommand]
	private void TestAttribute(string testParam)
	{
		Debug.Log($"This is a method that was registered using an Attribute. The parameter is: {testParam}");
	}
	
	[DevCommand("TestNamedCommand", "A command created by using the attribute", category = "Test", targetType = TargetType.Single)]
	private void TestAttribute3()
	{
		Debug.Log($"This is a method that was registered using an Attribute. This example uses a custom name");
	}
	
	#endregion
}
